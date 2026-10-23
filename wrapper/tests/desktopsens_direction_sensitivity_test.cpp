#include "../desktopsens_runtime.h"

#include <array>
#include <cmath>
#include <cstdlib>
#include <iostream>

namespace
{
    bool near(double actual, double expected, double epsilon = 1e-9)
    {
        return std::abs(actual - expected) <= epsilon;
    }

    int fail(const char* message)
    {
        std::cerr << "FAIL: " << message << '\n';
        return EXIT_FAILURE;
    }
}

int main()
{
    std::array<double, desktop_sens::AngularZoneCount> sensitivity{};
    std::array<double, desktop_sens::CellCount> acceleration{};
    std::array<double, desktop_sens::CellCount> rotation{};

    // Runtime defaults are neutral unless a cell is explicitly configured.
    sensitivity.fill(1.0);
    acceleration.fill(1.0);
    rotation.fill(0.0);

    // Screen/mouse convention used by atan2(dy, dx):
    // right = 0 degrees => axis 0; left = 180 degrees => axis 16.
    sensitivity[0] = 2.0;
    sensitivity[16] = 0.5;

    const desktop_sens::profile_view profile{
        sensitivity.data(), acceleration.data(), rotation.data()
    };

    desktop_sens::runtime runtime;

    const auto right = runtime.process(100, 0, 800, 10.0, profile);
    if (right.active_axis != 0)
        return fail("rightward movement should select axis 0");
    if (!near(right.dx, 200.0) || !near(right.dy, 0.0))
        return fail("rightward movement should use sensitivity 2.0 (100 -> 200)");

    runtime.reset();
    const auto left = runtime.process(-100, 0, 800, 10.0, profile);
    if (left.active_axis != 16)
        return fail("leftward movement should select axis 16");
    if (!near(left.dx, -50.0) || !near(left.dy, 0.0))
        return fail("leftward movement should use sensitivity 0.5 (-100 -> -50)");

    if (near(std::abs(right.dx), std::abs(left.dx)))
        return fail("left and right sensitivities should produce different magnitudes");

    std::cout << "PASS: right axis 0 uses 2.0x; left axis 16 uses 0.5x.\n";
    std::cout << "Right output: (" << right.dx << ", " << right.dy << ")\n";
    std::cout << "Left output:  (" << left.dx << ", " << left.dy << ")\n";
    std::cout << "Scope: standalone DesktopSens runtime math only; not live driver integration.\n";
    return EXIT_SUCCESS;
}
