#import "IndoorArHost.h"

static NSString *sRequest;
static void (^sHandler)(NSString *json);

@implementation IndoorArHost

+ (void)setRequest:(NSString *)json {
    sRequest = [json copy];
}

+ (void)setResultHandler:(void (^)(NSString *))handler {
    sHandler = [handler copy];
}

@end

extern "C" {

const char *IndoorArReadRequest(void) {
    return sRequest.length ? sRequest.UTF8String : NULL;
}

void IndoorArDeliverResult(const char *json) {
    NSString *body = json ? [NSString stringWithUTF8String:json] : @"";
    void (^handler)(NSString *) = sHandler;
    if (handler == nil) return;
    sHandler = nil;
    if ([NSThread isMainThread]) handler(body);
    else dispatch_async(dispatch_get_main_queue(), ^{ handler(body); });
}

}
