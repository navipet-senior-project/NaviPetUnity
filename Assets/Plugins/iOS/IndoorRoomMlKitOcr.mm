#import <Foundation/Foundation.h>
#import <CoreGraphics/CoreGraphics.h>
#import <UIKit/UIKit.h>
#if __has_include(<MLKitTextRecognition/MLKitTextRecognition.h>)
#import <MLKitTextRecognition/MLKitTextRecognition.h>
#import <MLKitTextRecognitionCommon/MLKitTextRecognitionCommon.h>
#import <MLKitVision/MLKitVision.h>
#define INDOOR_AR_MLKIT 1
#endif
#include <atomic>
#include <cstdint>
#include <cstddef>

extern "C" void UnitySendMessage(const char *object, const char *method, const char *message);

static std::atomic<bool> sBusy(false);

static void SendMlKitMessage(NSString *object, NSString *method, NSString *message) {
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
static bool MlKitModelPresent() {
    static bool present;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        present = [[NSBundle mainBundle] pathForResource:@"LatinOCRResources" ofType:@"bundle"] != nil
            || [[NSBundle bundleForClass:[MLKTextRecognizer class]] pathForResource:@"LatinOCRResources" ofType:@"bundle"] != nil;
    });
    return present;
}

static NSDictionary *RecognizeWithMlKit(NSData *pixels, int width, int height, int rotation,
                                        NSString **failure) {
    if (!MlKitModelPresent()) {
        *failure = @"model_unavailable";
        return nil;
    }
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
    if (image == nullptr) {
        *failure = @"image_conversion_failed";
        return nil;
    }
    MLKVisionImage *input = [[MLKVisionImage alloc] initWithImage:[UIImage imageWithCGImage:image]];
    CGImageRelease(image);
    input.orientation = UIImageOrientationUp;

    static MLKTextRecognizer *recognizer;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        recognizer = [MLKTextRecognizer textRecognizerWithOptions:[[MLKTextRecognizerOptions alloc] init]];
    });
    NSError *error = nil;
    MLKText *result = [recognizer resultsInImage:input error:&error];
    if (result == nil) {
        *failure = error.localizedDescription ?: @"recognition_failed";
        return nil;
    }

    NSMutableArray<NSDictionary *> *tokens = [NSMutableArray array];
    for (MLKTextBlock *block in result.blocks) {
        for (MLKTextLine *line in block.lines) {
            for (MLKTextElement *element in line.elements) {
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
    return @{ @"text": result.text ?: @"", @"width": @(uprightWidth),
        @"height": @(uprightHeight), @"rotation": @(rotation), @"tokens": tokens };
}
#endif

extern "C" int IndoorArRecognizeMlKitText(const uint8_t *rgba, int width, int height,
                                            int orientation, const char *callbackObject) {
    if (rgba == nullptr || callbackObject == nullptr || width < 1 || height < 1
        || width > 1920 || height > 1440 || (uint64_t)width * height * 4 > SIZE_MAX) return 0;
    bool expected = false;
    if (!sBusy.compare_exchange_strong(expected, true)) return 0;

    const size_t length = (size_t)width * height * 4;
    NSData *pixels = [NSData dataWithBytes:rgba length:length];
    NSString *object = [NSString stringWithUTF8String:callbackObject];
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        @autoreleasepool {
            const int rotation = orientation == 6 ? 90 : orientation == 3 ? 180 : orientation == 8 ? 270 : 0;
            NSString *failure = @"recognizer_unavailable";
#ifdef INDOOR_AR_MLKIT
            NSDictionary *frame = RecognizeWithMlKit(pixels, width, height, rotation, &failure);
            NSData *json = frame == nil ? nil : [NSJSONSerialization dataWithJSONObject:frame options:0 error:nil];
            if (json != nil)
                SendMlKitMessage(object, @"OnMlKitText", [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding]);
            else
                SendMlKitMessage(object, @"OnMlKitError", failure);
#else
            SendMlKitMessage(object, @"OnMlKitError", failure);
#endif
        }
        sBusy.store(false);
    });
    return 1;
}
