#import <Foundation/Foundation.h>
#import <Vision/Vision.h>
#import <CoreGraphics/CoreGraphics.h>
#import <ImageIO/ImageIO.h>
// Google ML Kit is used when its pod is in the export (IndoorArOcrDependencies.xml); Apple Vision otherwise.
#if __has_include(<MLKitTextRecognition/MLKitTextRecognition.h>)
#define INDOOR_AR_MLKIT 1
#import <UIKit/UIKit.h>
#import <MLKitTextRecognition/MLKitTextRecognition.h>
#import <MLKitTextRecognitionCommon/MLKitTextRecognitionCommon.h>
#import <MLKitVision/MLKitVision.h>
#endif
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

static CGImageRef CreateImage(NSData *pixels, int width, int height) {
    CGColorSpaceRef colorSpace = CGColorSpaceCreateDeviceRGB();
    CGDataProviderRef provider = CGDataProviderCreateWithCFData((__bridge CFDataRef)pixels);
    CGImageRef image = CGImageCreate(width, height, 8, 32, width * 4, colorSpace,
        kCGBitmapByteOrderDefault | kCGImageAlphaNoneSkipLast, provider, nullptr, false,
        kCGRenderingIntentDefault);
    CGDataProviderRelease(provider);
    CGColorSpaceRelease(colorSpace);
    return image;
}

#ifdef INDOOR_AR_MLKIT
// ML Kit looks for its model in the app's main bundle or beside the code that links it.
static bool MlKitModelPresent() {
    static bool present;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        present = [[NSBundle mainBundle] pathForResource:@"LatinOCRResources" ofType:@"bundle"] != nil
            || [[NSBundle bundleForClass:[MLKTextRecognizer class]] pathForResource:@"LatinOCRResources" ofType:@"bundle"] != nil;
    });
    return present;
}

/// Reads the frame with Google ML Kit. Nil when it cannot, so the caller falls back to Apple Vision.
static NSDictionary *RecognizeWithMlKit(NSData *pixels, int width, int height, int rotation) {
    if (!MlKitModelPresent()) return nil;
    // The pixels are turned upright here, so the corners ML Kit returns need no orientation convention.
    const bool turned = rotation == 90 || rotation == 270;
    const int uprightWidth = turned ? height : width, uprightHeight = turned ? width : height;
    NSMutableData *upright = [NSMutableData dataWithLength:(size_t)width * height * 4];
    const uint32_t *from = (const uint32_t *)pixels.bytes;
    uint32_t *to = (uint32_t *)upright.mutableBytes;
    for (int y = 0; y < uprightHeight; y++) {
        for (int x = 0; x < uprightWidth; x++) {
            int sx = x, sy = y;
            if (rotation == 90) { sx = y; sy = height - 1 - x; }
            else if (rotation == 180) { sx = width - 1 - x; sy = height - 1 - y; }
            else if (rotation == 270) { sx = width - 1 - y; sy = x; }
            *to++ = from[(size_t)sy * width + sx];
        }
    }
    CGImageRef image = CreateImage(upright, uprightWidth, uprightHeight);
    if (image == nullptr) return nil;
    MLKVisionImage *input = [[MLKVisionImage alloc] initWithImage:[UIImage imageWithCGImage:image]];
    CGImageRelease(image);
    input.orientation = UIImageOrientationUp;

    static MLKTextRecognizer *recognizer;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        recognizer = [MLKTextRecognizer textRecognizerWithOptions:[[MLKTextRecognizerOptions alloc] init]];
    });
    MLKText *result = [recognizer resultsInImage:input error:nil];
    if (result == nil) return nil;

    NSMutableArray<NSDictionary *> *tokens = [NSMutableArray array];
    for (MLKTextBlock *block in result.blocks) {
        for (MLKTextLine *line in block.lines) {
            for (MLKTextElement *element in line.elements) {
                // ML Kit's corners are pixels of the image it was given: top-left, then clockwise.
                if (element.cornerPoints.count != 4) continue;
                NSMutableArray<NSNumber *> *corners = [NSMutableArray arrayWithCapacity:8];
                for (NSValue *point in element.cornerPoints) {
                    [corners addObject:@(point.CGPointValue.x)];
                    [corners addObject:@(point.CGPointValue.y)];
                }
                [tokens addObject:@{ @"text": element.text ?: @"", @"corners": corners }];
            }
        }
    }
    return @{ @"engine": @"mlkit", @"text": result.text ?: @"", @"width": @(uprightWidth),
        @"height": @(uprightHeight), @"rotation": @(rotation), @"tokens": tokens };
}
#endif

static NSDictionary *RecognizeWithVision(NSData *pixels, int width, int height, int orientation,
                                         int rotation, NSString **failure) {
    CGImageRef image = CreateImage(pixels, width, height);
    if (image == nullptr) {
        *failure = @"image_conversion_failed";
        return nil;
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
    if (!succeeded) {
        *failure = @"recognition_failed";
        return nil;
    }

    // Orientations 5 to 8 turn the image a quarter, so the upright image swaps width and height.
    const bool turned = orientation >= 5;
    const double uprightWidth = turned ? height : width, uprightHeight = turned ? width : height;
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
    return @{ @"engine": @"vision", @"text": [lines componentsJoinedByString:@"\n"],
        @"width": @((int)uprightWidth), @"height": @((int)uprightHeight),
        @"rotation": @(rotation), @"tokens": tokens };
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
            const int rotation = orientation == 6 ? 90 : orientation == 3 ? 180 : orientation == 8 ? 270 : 0;
            NSString *failure = @"result_encoding_failed";
            NSDictionary *frame = nil;
#ifdef INDOOR_AR_MLKIT
            frame = RecognizeWithMlKit(pixels, width, height, rotation);
#endif
            if (frame == nil) frame = RecognizeWithVision(pixels, width, height, orientation, rotation, &failure);
            NSData *json = frame == nil ? nil : [NSJSONSerialization dataWithJSONObject:frame options:0 error:nil];
            if (json != nil)
                SendVisionMessage(object, @"OnVisionText",
                    [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding]);
            else
                SendVisionMessage(object, @"OnVisionError", failure);
        }
        sBusy.store(false);
    });
    return 1;
}
