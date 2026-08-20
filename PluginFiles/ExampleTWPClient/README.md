ExampleTWPClient
==================

This folder contains an example KSP plugin source file demonstrating how another mod can call the TransferWindowPlanner API using the provided reflection wrapper.

Files
- ExampleTWPClient.cs - a small KSPAddon that waits for TWP to load, requests a transfer calculation (Kerbin -> Mun example) and attempts to register a KAC alarm using the reflection wrapper.

Usage
1. Compile the example into a plugin DLL and place it into your GameData/whatever/Plugins folder or use the source as a reference in your own mod.
2. Ensure TransferWindowPlanner (the TWP mod) is installed and the API branch code (TransferAPI and TransferAPI_ReflectionWrapper) is present in the running game.
3. In flight, the example will request a transfer for Kerbin->Mun ~1 day from now and log the results to the KSP console.

Notes
- The example uses TWP_ReflectionWrapper.CalculateTransferAsync to avoid blocking the main thread. If you use CalculateTransfer (sync) be careful not to run expensive work on the Unity main thread.
- The example calls TWP_ReflectionWrapper.TryRegisterKACAlarm. That method attempts to register the alarm via TWP's KAC helper; it may return false if KAC isn't installed or if signatures don't match. In that case the returned alarm DTO can be registered with your own KAC wrapper.
- Times are in UT seconds. Altitudes are in meters. Inclination in radians.

If you want, you can extend the example to show results in a small GUI, spawn a notification, or automatically schedule burns.
