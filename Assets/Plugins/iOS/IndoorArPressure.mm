#import <Foundation/Foundation.h>
#import <CoreMotion/CoreMotion.h>
#include <atomic>

// Latest altimeter pressure for the indoor floor-change check. Core Motion delivers about once a second on the main
// queue; Unity polls the newest value and a counter. Refused Motion & Fitness access or a phone without a barometer
// leaves the counter at 0, which Unity reads as "no pressure".
static CMAltimeter *sAltimeter;
static std::atomic<float> sHpa(0.0f);
static std::atomic<int> sSeq(0);

extern "C" {

void IndoorArPressureStart(void) {
    if (sAltimeter != nil || ![CMAltimeter isRelativeAltitudeAvailable]) return;
    sSeq = 0;
    sAltimeter = [[CMAltimeter alloc] init];
    [sAltimeter startRelativeAltitudeUpdatesToQueue:[NSOperationQueue mainQueue]
                                        withHandler:^(CMAltitudeData *data, NSError *error) {
        if (data == nil || error != nil) return;
        sHpa = data.pressure.floatValue * 10.0f; // kPa -> hPa
        sSeq++;
    }];
}

void IndoorArPressureStop(void) {
    [sAltimeter stopRelativeAltitudeUpdates];
    sAltimeter = nil;
}

int IndoorArReadPressure(float *hpa) {
    *hpa = sHpa;
    return sSeq;
}

}
