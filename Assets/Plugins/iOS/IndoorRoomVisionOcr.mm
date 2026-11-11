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
        || width > 640 || height > 480 || (uint64_t)width * height * 4 > SIZE_MAX) return false;
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
            VNImageRequestHandler *handler = [[VNImageRequestHandler alloc] initWithCGImage:image
                orientation:(CGImagePropertyOrientation)orientation options:@{}];
            BOOL succeeded = [handler performRequests:@[request] error:&error];
            CGImageRelease(image);

            if (succeeded) {
                NSMutableArray<NSString *> *lines = [NSMutableArray array];
                for (VNRecognizedTextObservation *observation in request.results) {
                    VNRecognizedText *candidate = [observation topCandidates:1].firstObject;
                    if (candidate.string.length > 0) [lines addObject:candidate.string];
                }
                SendVisionMessage(object, @"OnVisionText", [lines componentsJoinedByString:@"\n"]);
            } else {
                SendVisionMessage(object, @"OnVisionError", @"recognition_failed");
            }
        }
        sBusy.store(false);
    });
    return 1;
}
