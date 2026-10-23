#pragma once

#define NOMINMAX

#ifdef _KERNEL_MODE
#include <ntddk.h>
#else
#include <Windows.h>
#endif

namespace rawaccel {
    constexpr ULONG READ = (ULONG)CTL_CODE(0x8888u, 0x888, METHOD_BUFFERED, FILE_ANY_ACCESS);
    constexpr ULONG WRITE = (ULONG)CTL_CODE(0x8888u, 0x889, METHOD_BUFFERED, FILE_ANY_ACCESS);
    constexpr ULONG GET_VERSION = (ULONG)CTL_CODE(0x8888u, 0x88a, METHOD_BUFFERED, FILE_ANY_ACCESS);
    constexpr ULONG DESKTOPSENS_SET = (ULONG)CTL_CODE(0x8888u, 0x88b, METHOD_BUFFERED, FILE_ANY_ACCESS);

    constexpr ULONG DESKTOPSENS_ANGLE_ZONES = 32;
    constexpr ULONG DESKTOPSENS_DISTANCE_ZONES = 23;
    constexpr ULONG DESKTOPSENS_CELL_COUNT = DESKTOPSENS_ANGLE_ZONES * DESKTOPSENS_DISTANCE_ZONES;

    // Fixed-size, version-independent payload shared by the GUI and kernel driver.
    // The payload is copied into nonpaged driver-owned storage before the IOCTL returns.
    struct desktop_sens_profile {
        ULONG enabled;
        double dpi;
        double sensitivity[DESKTOPSENS_ANGLE_ZONES];
        double acceleration[DESKTOPSENS_CELL_COUNT];
        double rotation_degrees[DESKTOPSENS_CELL_COUNT];
    };
}