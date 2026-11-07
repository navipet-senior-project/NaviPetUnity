#import "IndoorArHost.h"

// Declared in the trampoline. A paused controller is an unloaded embedded player.
extern "C" {
void *GetAppController(void);
int UnityIsPaused(void);
void UnityLoadApplicationFromSceneLessState(void);
void UnitySuppressPauseMessage(void);
void UnityPause(int pause);
}

static NSString *sRequest;
static void (^sHandler)(NSString *json);

static void ResumeUnloadedPlayer(void) {
    // The host calls runEmbedded only while appController is nil. After
    // unloadApplication the controller is still alive and paused, so the host
    // shows the last frame and never loads the scene that would read sRequest.
    if (GetAppController() == NULL || !UnityIsPaused()) return;
    UnityLoadApplicationFromSceneLessState();
    UnitySuppressPauseMessage();
    UnityPause(0);
}

@implementation IndoorArHost

+ (void)setRequest:(NSString *)json {
    sRequest = [json copy];
}

+ (void)setResultHandler:(void (^)(NSString *))handler {
    sHandler = [handler copy];
    ResumeUnloadedPlayer();
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
