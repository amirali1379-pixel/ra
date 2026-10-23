#pragma once

#include <array>
#include <cstddef>

namespace desktop_sens
{
    constexpr std::size_t AngularZoneCount = 32;
    constexpr std::size_t DistanceZoneCount = 23;
    constexpr std::size_t CellCount = AngularZoneCount * DistanceZoneCount;
    constexpr double MicroZoneMaxCm = 3.0;

    struct profile_view
    {
        const double* sensitivity; // 32 factors, indexed by movement direction
        const double* acceleration; // 32 * 23 factors, angle-major
        const double* rotation_degrees; // 32 * 23 signed corrections
    };

    struct process_result
    {
        double dx = 0.0;
        double dy = 0.0;
        int active_axis = -1;
        int distance_zone = 0;
        double stroke_distance_cm = 0.0;
    };

    // Standalone user-mode math/state. This module does not read/inject mouse input and
    // does not issue IOCTLs; the caller owns the single live input/output path.
    class runtime
    {
    public:
        process_result process(
            int dx, int dy, int dpi, double elapsed_ms,
            const profile_view& profile) noexcept;

        void reset() noexcept;

    private:
        double stroke_distance_cm_ = 0.0;
        int previous_axis_ = -1;
        double idle_ms_ = 0.0;
    };

    int angle_to_zone(double angle_degrees) noexcept;
    int distance_to_zone(double distance_cm) noexcept;
}
