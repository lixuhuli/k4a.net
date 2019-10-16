﻿using System.Runtime.InteropServices;

namespace K4AdotNet.BodyTracking
{
    // Defined in k4abttypes.h:
    // typedef struct _k4abt_tracker_configuration_t
    // {
    //     k4abt_sensor_orientation_t sensor_orientation;
    //     bool cpu_only_mode;
    // } k4abt_tracker_configuration_t;
    //
    /// <summary>Configuration parameters for a k4abt body tracker.</summary>
    /// <remarks>Used by <see cref="Tracker.Tracker"/> to specify the configuration of the k4abt tracker.</remarks>
    /// <seealso cref="Tracker.Tracker"/>
    [StructLayout(LayoutKind.Sequential)]
    public struct TrackerConfiguration
    {
        /// <summary>The sensor mounting orientation type.</summary>
        /// <remarks>Setting the correct orientation can help the body tracker to achieve more accurate body tracking results.</remarks>
        public SensorOrientation SensorOrientation;

        /// <summary>Specify whether to use CPU only mode or GPU mode to run the tracker.</summary>
        /// <remarks>
        /// The CPU only mode doesn't require the machine to have a GPU to run this SDK.
        /// But it will be much slower than the GPU mode.
        /// </remarks>
        [MarshalAs(UnmanagedType.I1)]
        public bool CpuOnlyMode;

        // static const k4abt_tracker_configuration_t K4ABT_TRACKER_CONFIG_DEFAULT = { K4ABT_SENSOR_ORIENTATION_DEFAULT, false };
        //
        /// <summary>Default configuration setting for k4abt tracker.</summary>
        /// <remarks>Use this setting to initialize a <see cref="TrackerConfiguration"/> to a default state.</remarks>
        public static readonly TrackerConfiguration Default = new TrackerConfiguration {
            SensorOrientation = SensorOrientation.Default,
            CpuOnlyMode = false,
        };
    }
}
