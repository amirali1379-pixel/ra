#include "desktopsens_runtime.h"

#include <algorithm>
#include <cmath>

namespace desktop_sens
{
    namespace
    {
        constexpr double Pi = 3.14159265358979323846;
        constexpr double DegreesPerZone = 360.0 / static_cast<double>(AngularZoneCount);
        constexpr double InchesToCm = 2.54;
        constexpr double StrokeResetIdleMs = 150.0;

        double finite_positive_or(double value, double fallback) noexcept
        {
            return std::isfinite(value) && value > 0.0 ? value : fallback;
        }

        double finite_or(double value, double fallback) noexcept
        {
            return std::isfinite(value) ? value : fallback;
        }

        double normalize_degrees(double value) noexcept
        {
            value = std::fmod(value, 360.0);
            if (value < 0.0) value += 360.0;
            return value;
        }

        double normalize_signed_degrees(double value) noexcept
        {
            value = std::fmod(value, 360.0);
            if (value > 180.0) value -= 360.0;
            if (value < -180.0) value += 360.0;
            return value;
        }
    }

    int angle_to_zone(double angle_degrees) noexcept
    {
        const double normalized = normalize_degrees(angle_degrees);
        const int zone = static_cast<int>(std::floor(normalized / DegreesPerZone));
        return std::clamp(zone, 0, static_cast<int>(AngularZoneCount) - 1);
    }

    int distance_to_zone(double distance_cm) noexcept
    {
        distance_cm = std::max(0.0, finite_or(distance_cm, 0.0));
        if (distance_cm < 3.0)
            return std::clamp(static_cast<int>(distance_cm / 0.5), 0, 5);
        if (distance_cm < 10.0)
            return 6 + std::clamp(static_cast<int>((distance_cm - 3.0) / 1.0), 0, 6);
        if (distance_cm < 20.0)
            return 13 + std::clamp(static_cast<int>((distance_cm - 10.0) / 2.0), 0, 4);
        if (distance_cm < 40.0)
            return 18 + std::clamp(static_cast<int>((distance_cm - 20.0) / 4.0), 0, 4);
        return 22;
    }

    void runtime::reset() noexcept
    {
        stroke_distance_cm_ = 0.0;
        previous_axis_ = -1;
        idle_ms_ = 0.0;
    }

    process_result runtime::process(
        int dx, int dy, int dpi, double elapsed_ms,
        const profile_view& profile) noexcept
    {
        process_result result{};
        if ((dx == 0 && dy == 0) || dpi <= 0 ||
            profile.sensitivity == nullptr ||
            profile.acceleration == nullptr ||
            profile.rotation_degrees == nullptr)
        {
            idle_ms_ += std::max(0.0, finite_or(elapsed_ms, 0.0));
            if (idle_ms_ >= StrokeResetIdleMs) reset();
            result.stroke_distance_cm = stroke_distance_cm_;
            return result;
        }

        const double raw_angle = std::atan2(static_cast<double>(dy), static_cast<double>(dx)) * 180.0 / Pi;
        const int axis = angle_to_zone(raw_angle);
        const double step_cm = std::hypot(static_cast<double>(dx), static_cast<double>(dy)) /
            static_cast<double>(dpi) * InchesToCm;
        const double dt = std::max(0.0, finite_or(elapsed_ms, 0.0));

        // A new direction zone starts a new stroke, so one active axis owns each move.
        // An idle gap also resets distance without depending on RawAccel being enabled.
        if (axis != previous_axis_ || idle_ms_ >= StrokeResetIdleMs)
            stroke_distance_cm_ = 0.0;
        stroke_distance_cm_ += step_cm;
        previous_axis_ = axis;
        idle_ms_ = 0.0;

        const int distance_zone = distance_to_zone(stroke_distance_cm_);
        const std::size_t cell = static_cast<std::size_t>(axis) * DistanceZoneCount +
            static_cast<std::size_t>(distance_zone);

        const double base = finite_positive_or(profile.sensitivity[axis], 1.0);
        const double accel = stroke_distance_cm_ <= MicroZoneMaxCm
            ? 1.0
            : finite_positive_or(profile.acceleration[cell], 1.0);
        const double rotation = normalize_signed_degrees(finite_or(profile.rotation_degrees[cell], 0.0));
        const double radians = rotation * Pi / 180.0;
        const double rotated_x = static_cast<double>(dx) * std::cos(radians) -
            static_cast<double>(dy) * std::sin(radians);
        const double rotated_y = static_cast<double>(dx) * std::sin(radians) +
            static_cast<double>(dy) * std::cos(radians);
        const double scale = base * accel;

        result.dx = rotated_x * scale;
        result.dy = rotated_y * scale;
        result.active_axis = axis;
        result.distance_zone = distance_zone;
        result.stroke_distance_cm = stroke_distance_cm_;
        return result;
    }
}
