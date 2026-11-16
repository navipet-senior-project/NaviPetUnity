// Planar PnP for IndoorSignPose: OpenCV's IPPE solver, both of its solutions, and each refined by Levenberg-Marquardt.
// tools/build-sign-pnp.sh builds it into libindoorsignpnp.a for iOS and libindoorsignpnp.so for Android.
#include <opencv2/calib3d.hpp>
#include <vector>

// Nine values: the first two rotation columns, then the translation.
static void Write(const cv::Mat& rvec, const cv::Mat& tvec, double* out)
{
    cv::Mat r;
    cv::Rodrigues(rvec, r);
    for (int j = 0; j < 3; j++)
    {
        out[j] = r.at<double>(j, 0);
        out[3 + j] = r.at<double>(j, 1);
        out[6 + j] = tvec.at<double>(j);
    }
}

// plane8: the sign's four corners (x, y) in metres on its plane. image8: their pixels, origin top-left, y down.
// k4: fx, fy, cx, cy. out36: IPPE solution s at 9 * s and its refinement at 9 * (2 + s), in OpenCV's camera frame
// (y down). Returns the solution count, at most 2, and 0 when OpenCV finds none.
// Both are needed: near a frontal view raw IPPE can be centimetres off where refinement is exact, and far away
// refinement can pull both flips into one pose, hiding the ambiguity the raw pair shows.
extern "C" int IndoorSignSolveIppe(const double* plane8, const double* image8, const double* k4, double* out36)
{
    try
    {
        std::vector<cv::Point3d> object;
        std::vector<cv::Point2d> image;
        for (int i = 0; i < 4; i++)
        {
            object.emplace_back(plane8[2 * i], plane8[2 * i + 1], 0.0);
            image.emplace_back(image8[2 * i], image8[2 * i + 1]);
        }
        const cv::Matx33d k(k4[0], 0, k4[2], 0, k4[1], k4[3], 0, 0, 1);
        std::vector<cv::Mat> rvecs, tvecs;
        const int found = cv::solvePnPGeneric(object, image, k, cv::noArray(), rvecs, tvecs, false, cv::SOLVEPNP_IPPE);
        const int count = found < 2 ? found : 2;
        for (int s = 0; s < count; s++)
        {
            Write(rvecs[s], tvecs[s], out36 + 9 * s);
            cv::Mat r = rvecs[s].clone(), t = tvecs[s].clone();
            cv::solvePnPRefineLM(object, image, k, cv::noArray(), r, t);
            Write(r, t, out36 + 9 * (2 + s));
        }
        return count;
    }
    catch (...)
    {
        // Nothing may unwind into IL2CPP; a degenerate quad is just no pose.
        return 0;
    }
}
