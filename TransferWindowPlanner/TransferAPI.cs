using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace TransferWindowPlanner
{
    // Public API for other mods to request transfer calculations from TWP.
    // Units:
    //  - UT times are seconds since KSP epoch (double UT).
    //  - Altitudes are in meters.
    //  - Inclinations are in radians.
    //  - Delta-V in meters/second.
    public static class TWP_API
    {
        // Request object other mods should populate
        public class TransferRequest
        {
            public string OriginBodyName { get; set; }        // e.g. "Kerbin"
            public string DestinationBodyName { get; set; }   // e.g. "Mun"
            public double DepartureUT { get; set; }           // UT of departure window (seconds)
            public double TimeOfFlightUT { get; set; }        // travel time (seconds)
            public double InitialOrbitAltitude { get; set; }  // meters
            public double InitialOrbitInclinationRad { get; set; } // radians
            public double FinalOrbitAltitude { get; set; }    // meters (0 for flyby)
        }

        // Result returned from a calculation
        public class TransferResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }

            public double TotalDeltaV { get; set; }          // m/s
            public double DepartureUT { get; set; }          // echo of requested departure UT
            public double TimeOfFlightUT { get; set; }       // echo of requested TOF

            // Basic vectors (components in local coordinate space used by TransferDeltaV)
            public Vec3d EjectionVector { get; set; }
            public Vec3d PeriapsisDirection { get; set; }
            public double PhaseAngleRad { get; set; }

            // Raw detail object if you want it (optional, may be null)
            public object RawTransferInfo { get; set; }

            // Convenience: suggested alarm time (UT) for an ejection burn or departure event.
            // Caller can use this to create a KAC alarm. Default is DepartureUT.
            public double SuggestedAlarmUT { get; set; }
        }

        // Small double-precision vector to avoid coupling to internal types.
        public struct Vec3d
        {
            public double x, y, z;
            public Vec3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
            public static Vec3d FromVector3(Vector3 v) => new Vec3d(v.x, v.y, v.z);
        }

        // KAC alarm spec: minimal info returned to caller so they can register with KAC.
        public class KACAlarmSpec
        {
            public string Name { get; set; }
            public double AlarmUT { get; set; }      // UT at which alarm should fire
            public int LeadSeconds { get; set; }     // lead-time in seconds before the alarm event (optional)
            public string Notes { get; set; }        // free-form text
            public double PhaseAngleRad { get; set; } // Phase angle in radians for this transfer (optional)
            // Add any other KAC-relevant fields you need (repeat, color, vessel id, etc.)
        }

        // Synchronous calculation. Returns TransferResult immediately (may take time).
        public static TransferResult CalculateTransfer(TransferRequest req)
        {
            var res = new TransferResult()
            {
                Success = false,
                DepartureUT = req.DepartureUT,
                TimeOfFlightUT = req.TimeOfFlightUT,
                SuggestedAlarmUT = req.DepartureUT
            };

            try
            {
                // Resolve bodies
                var origin = FlightGlobals.Bodies.FirstOrDefault(b => b.bodyName == req.OriginBodyName);
                var dest = FlightGlobals.Bodies.FirstOrDefault(b => b.bodyName == req.DestinationBodyName);
                if (origin == null || dest == null)
                {
                    res.ErrorMessage = $"Origin or destination body not found: {req.OriginBodyName}, {req.DestinationBodyName}";
                    return res;
                }

                // Call the Lambert solver overload that provides TransferDetails (preferred) and fall back to the simpler overload.
                TransferDeltaVInfo info = default(TransferDeltaVInfo);
                TransferDetails details = null;

                try
                {
                    // Preferred overload: returns TransferDeltaVInfo and outputs a TransferDetails object with richer data
                    info = LambertSolver.TransferDeltaV(origin, dest, req.DepartureUT, req.TimeOfFlightUT,
                        req.InitialOrbitAltitude, req.InitialOrbitInclinationRad, req.FinalOrbitAltitude, out details);
                }
                catch
                {
                    // Fallback: call the overload that returns TransferDeltaVInfo only
                    info = LambertSolver.TransferDeltaV(origin, dest, req.DepartureUT, req.TimeOfFlightUT,
                        req.InitialOrbitAltitude, req.InitialOrbitInclinationRad, req.FinalOrbitAltitude);
                }

                if (details != null)
                {
                    // Use the rich TransferDetails when available
                    res.TotalDeltaV = details.DVTotal;
                    var ev = details.EjectionVector;
                    res.EjectionVector = new Vec3d(ev.x, ev.y, ev.z);
                    var pd = details.PeriDirection;
                    res.PeriapsisDirection = new Vec3d(pd.x, pd.y, pd.z);
                    res.PhaseAngleRad = details.PhaseAngle;
                    res.RawTransferInfo = details;
                }
                else if (!double.IsNaN(info.Total) && !double.IsInfinity(info.Total))
                {
                    // Limited info only
                    res.TotalDeltaV = info.Total;
                    res.RawTransferInfo = info;
                    // Ejection vector, periapsis direction and phase angle are not available from the simple TransferDeltaVInfo
                    res.EjectionVector = new Vec3d(0, 0, 0);
                    res.PeriapsisDirection = new Vec3d(0, 0, 0);
                    res.PhaseAngleRad = 0;
                }
                else
                {
                    res.ErrorMessage = "Lambert solver returned no result.";
                    return res;
                }

                res.Success = true;
                res.SuggestedAlarmUT = req.DepartureUT; // default suggestion
                return res;
            }
            catch (Exception ex)
            {
                res.ErrorMessage = ex.Message + (ex.StackTrace != null ? (" " + ex.StackTrace) : "");
                return res;
            }
        }

        // Async calculation. Useful for callers that don't want to block the game thread.
        public static Task<TransferResult> CalculateTransferAsync(TransferRequest req)
        {
            return Task.Run(() => CalculateTransfer(req));
        }

        // Build a KAC alarm spec for this transfer (caller can register it with KAC)
        public static KACAlarmSpec BuildKACAlarmForTransfer(TransferResult tr, int leadSeconds = 0, string alarmName = null, string notes = null)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            var name = alarmName ?? $"TWP Transfer: {tr.DepartureUT:F0} -> Δv {tr.TotalDeltaV:F1} m/s";
            return new KACAlarmSpec()
            {
                Name = name,
                AlarmUT = tr.SuggestedAlarmUT,
                LeadSeconds = leadSeconds,
                Notes = notes ?? $"Suggested by TransferWindowPlanner. Total Δv: {tr.TotalDeltaV:F2} m/s",
                PhaseAngleRad = tr.PhaseAngleRad
            };
        }

        // Optional helper to attempt to register the alarm with KAC if available.
        // Because the KAC wrapper API signatures vary by KAC version/wrapper, this is a guarded helper.
        // If KAC wrapper is present and ready, call its create method; otherwise return false and caller can register.
        public static bool TryRegisterKACAlarm(KACAlarmSpec spec)
        {
            if (spec == null) return false;

            try
            {
                // Example guarded call. Replace the commented call with the real KACWrapper API call you have in the project.
                if (KACWrapper.APIReady)
                {
                    // PSEUDO-CODE:
                    // KACWrapper.KAC.CreateAlarm(spec.Name, spec.AlarmUT, spec.LeadSeconds, spec.Notes, spec.PhaseAngleRad);
                    //
                    // The actual wrapper methods / parameter list must be adjusted to match your KAC wrapper
                    // (look at the KAC wrapper in this project and hook the appropriate create/add method).
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
