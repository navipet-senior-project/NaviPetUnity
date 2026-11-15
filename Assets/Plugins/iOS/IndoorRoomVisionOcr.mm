#import <Foundation/Foundation.h>
#import <Vision/Vision.h>
#import <CoreGraphics/CoreGraphics.h>
#import <ImageIO/ImageIO.h>
#include <atomic>
#include <cstdint>
#include <cstddef>

extern "C" void UnitySendMessage(const char *object, const char *method, const char *message);

static std::atomic<bool> sBusy(false);

static void SendVisionMessage(NSString *object, NSString *method, NSString *message) {
    dispatch_async(dispatch_get_main_queue(), ^{
        UnitySendMessage(object.UTF8String, method.UTF8String, message.UTF8String);
    });
}

extern "C" int IndoorArRecognizeText(const uint8_t *rgba, int width, int height,
                                       int orientation, const char *callbackObject) {
    if (rgba == nullptr || callbackObject == nullptr || width < 1 || height < 1
        || width > 1920 || height > 1440 || (uint64_t)width * height * 4 > SIZE_MAX) return false;
    bool expected = false;
    if (!sBusy.compare_exchange_strong(expected, true)) return false;

    const size_t length = (size_t)width * height * 4;
    NSData *pixels = [NSData dataWithBytes:rgba length:length];
    NSString *object = [NSString stringWithUTF8String:callbackObject];
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        @autoreleasepool {
            CGColorSpaceRef colorSpace = CGColorSpaceCreateDeviceRGB();
            CGDataProviderRef provider = CGDataProviderCreateWithCFData((__bridge CFDataRef)pixels);
            CGImageRef image = CGImageCreate(width, height, 8, 32, width * 4, colorSpace,
                kCGBitmapByteOrderDefault | kCGImageAlphaLast, provider, nullptr, false,
                kCGRenderingIntentDefault);
            CGDataProviderRelease(provider);
            CGColorSpaceRelease(colorSpace);

            if (image == nullptr) {
                SendVisionMessage(object, @"OnVisionError", @"image_conversion_failed");
                sBusy.store(false);
                return;
            }

            NSError *error = nil;
            VNRecognizeTextRequest *request = [[VNRecognizeTextRequest alloc] init];
            request.recognitionLevel = VNRequestTextRecognitionLevelAccurate;
            request.usesLanguageCorrection = NO;
            // The default (1/32 of the image height) only reads a room number from arm's length.
            request.minimumTextHeight = 0.01;
            VNImageRequestHandler *handler = [[VNImageRequestHandler alloc] initWithCGImage:image
                orientation:(CGImagePropertyOrientation)orientation options:@{}];
            BOOL succeeded = [handler performRequests:@[request] error:&error];
            CGImageRelease(image);

            if (succeeded) {
                // Orientations 5 to 8 turn the image a quarter, so the upright image swaps width and height.
                const bool turned = orientation >= 5;
                const double uprightWidth = turned ? height : width, uprightHeight = turned ? width : height;
                const int rotation = orientation == 6 ? 90 : orientation == 3 ? 180 : orientation == 8 ? 270 : 0;
                NSMutableArray<NSString *> *lines = [NSMutableArray array];
                NSMutableArray<NSDictionary *> *tokens = [NSMutableArray array];
                NSCharacterSet *notDigits = [[NSCharacterSet characterSetWithCharactersInString:@"0123456789"] invertedSet];
                for (VNRecognizedTextObservation *observation in request.results) {
                    VNRecognizedText *candidate = [observation topCandidates:1].firstObject;
                    NSString *line = candidate.string;
                    if (line.length == 0) continue;
                    [lines addObject:line];
                    // One token per run of digits, with the four corners Vision gives that run.
                    NSUInteger at = 0;
                    while (at < line.length) {
                        NSRange rest = NSMakeRange(at, line.length - at);
                        NSUInteger start = [line rangeOfCharacterFromSet:notDigits.invertedSet options:0 range:rest].location;
                        if (start == NSNotFound) break;
                        NSUInteger end = [line rangeOfCharacterFromSet:notDigits options:0
                            range:NSMakeRange(start, line.length - start)].location;
                        if (end == NSNotFound) end = line.length;
                        NSRange run = NSMakeRange(start, end - start);
                        at = end;
                        VNRectangleObservation *box = [candidate boundingBoxForRange:run error:nil];
                        if (box == nil) continue;
                        // Vision's points are 0..1 from the bottom-left of the upright image.
                        CGPoint points[4] = { box.topLeft, box.topRight, box.bottomRight, box.bottomLeft };
                        NSMutableArray<NSNumber *> *corners = [NSMutableArray arrayWithCapacity:8];
                        for (int i = 0; i < 4; i++) {
                            [corners addObject:@(points[i].x * uprightWidth)];
                            [corners addObject:@((1.0 - points[i].y) * uprightHeight)];
                        }
                        [tokens addObject:@{ @"text": [line substringWithRange:run], @"corners": corners }];
                    }
                }
                NSData *json = [NSJSONSerialization dataWithJSONObject:@{
                    @"text": [lines componentsJoinedByString:@"\n"],
                    @"width": @((int)uprightWidth), @"height": @((int)uprightHeight),
                    @"rotation": @(rotation), @"tokens": tokens } options:0 error:nil];
                if (json != nil)
                    SendVisionMessage(object, @"OnVisionText",
                        [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding]);
                else
                    SendVisionMessage(object, @"OnVisionError", @"result_encoding_failed");
            } else {
                SendVisionMessage(object, @"OnVisionError", @"recognition_failed");
            }
        }
        sBusy.store(false);
    });
    return 1;
}
