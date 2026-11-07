#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

/// Host side of the indoor AR request/result bridge inside UnityFramework.
///
/// The request JSON is the same string Android stores in the
/// `navipet.indoorAr.request` intent extra. The result JSON is the same string
/// `UnityHostActivity.deliverResult` returns. Set both before the scene runs.
/// Delivering a result clears the request. On a later launch, setting both
/// reloads a player that unload left paused so the new request is what the
/// scene reads. The result handler is called at most once, on the main queue.
/// Do not log the request: a v2 request may carry an arToken.
@interface IndoorArHost : NSObject
+ (void)setRequest:(NSString *)json;
+ (void)setResultHandler:(void (^ _Nullable)(NSString *json))handler;
@end

NS_ASSUME_NONNULL_END
