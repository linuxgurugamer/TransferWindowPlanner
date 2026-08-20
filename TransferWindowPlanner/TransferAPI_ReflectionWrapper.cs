using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// Reflection wrapper for TransferWindowPlanner.TWP_API
// This wrapper lets other mods call into TransferWindowPlanner without a compile-time reference.
// Usage (example):
//   var req = new TWP_ReflectionWrapper.TransferRequestDTO { OriginBodyName="Kerbin", DestinationBodyName="Mun", DepartureUT=..., TimeOfFlightUT=..., InitialOrbitAltitude=100000, InitialOrbitInclinationRad=0, FinalOrbitAltitude=100000 };
//   var res = TWP_ReflectionWrapper.CalculateTransfer(req);
//   if (res.Success) { var alarm = TWP_ReflectionWrapper.BuildKACAlarmForTransfer(res); TWP_ReflectionWrapper.TryRegisterKACAlarm(alarm); }

public static class TWP_ReflectionWrapper
{
    public class TransferRequestDTO
    {
        public string OriginBodyName { get; set; }
        public string DestinationBodyName { get; set; }
        public double DepartureUT { get; set; }
        public double TimeOfFlightUT { get; set; }
        public double InitialOrbitAltitude { get; set; }
        public double InitialOrbitInclinationRad { get; set; }
        public double FinalOrbitAltitude { get; set; }
    }

    public class Vec3dDTO { public double x; public double y; public double z; }

    public class TransferResultDTO
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double TotalDeltaV { get; set; }
        public double DepartureUT { get; set; }
        public double TimeOfFlightUT { get; set; }
        public Vec3dDTO EjectionVector { get; set; }
        public Vec3dDTO PeriapsisDirection { get; set; }
        public double PhaseAngleRad { get; set; }
        public double SuggestedAlarmUT { get; set; }
        // RawTransferInfo left out intentionally to avoid typing issues across mods
    }

    public class KACAlarmSpecDTO
    {
        public string Name { get; set; }
        public double AlarmUT { get; set; }
        public int LeadSeconds { get; set; }
        public string Notes { get; set; }
    }

    static Type apiType;
    static Type nestedRequestType;
    static Type nestedResultType;
    static MethodInfo miCalculate;
    static MethodInfo miCalculateAsync;
    static MethodInfo miBuildAlarm;
    static MethodInfo miTryRegister;

    static bool EnsureLoaded()
    {
        if (apiType != null) return true;

        // Find the TWP_API type by full name across loaded assemblies
        apiType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); } catch { return new Type[0]; }
            })
            .FirstOrDefault(t => t.FullName == "TransferWindowPlanner.TWP_API");

        if (apiType == null) return false;

        // find nested types and methods
        nestedRequestType = apiType.GetNestedType("TransferRequest", BindingFlags.Public | BindingFlags.NonPublic) ?? apiType.GetNestedType("TransferRequestDTO", BindingFlags.Public | BindingFlags.NonPublic);
        nestedResultType = apiType.GetNestedType("TransferResult", BindingFlags.Public | BindingFlags.NonPublic);

        miCalculate = apiType.GetMethod("CalculateTransfer", BindingFlags.Public | BindingFlags.Static);
        miCalculateAsync = apiType.GetMethod("CalculateTransferAsync", BindingFlags.Public | BindingFlags.Static);
        miBuildAlarm = apiType.GetMethod("BuildKACAlarmForTransfer", BindingFlags.Public | BindingFlags.Static);
        miTryRegister = apiType.GetMethod("TryRegisterKACAlarm", BindingFlags.Public | BindingFlags.Static);

        return true;
    }

    public static bool IsAvailable() => EnsureLoaded();

    public static TransferResultDTO CalculateTransfer(TransferRequestDTO req)
    {
        if (!EnsureLoaded()) return new TransferResultDTO { Success = false, ErrorMessage = "TWP API not found" };
        if (miCalculate == null || nestedRequestType == null || nestedResultType == null)
            return new TransferResultDTO { Success = false, ErrorMessage = "TWP API calculate method not found" };

        // Create instance of nested request type and copy properties
        var requestObj = Activator.CreateInstance(nestedRequestType);
        void SetIfExists(string name, object value)
        {
            var pi = nestedRequestType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (pi != null && pi.CanWrite) pi.SetValue(requestObj, Convert.ChangeType(value, pi.PropertyType), null);
        }

        SetIfExists("OriginBodyName", req.OriginBodyName);
        SetIfExists("DestinationBodyName", req.DestinationBodyName);
        SetIfExists("DepartureUT", req.DepartureUT);
        SetIfExists("TimeOfFlightUT", req.TimeOfFlightUT);
        SetIfExists("InitialOrbitAltitude", req.InitialOrbitAltitude);
        SetIfExists("InitialOrbitInclinationRad", req.InitialOrbitInclinationRad);
        SetIfExists("FinalOrbitAltitude", req.FinalOrbitAltitude);

        // Invoke the calculate method
        var resultObj = miCalculate.Invoke(null, new object[] { requestObj });
        if (resultObj == null)
            return new TransferResultDTO { Success = false, ErrorMessage = "TWP Calculate returned null" };

        return MapResult(resultObj);
    }

    public static async Task<TransferResultDTO> CalculateTransferAsync(TransferRequestDTO req)
    {
        if (!EnsureLoaded()) return new TransferResultDTO { Success = false, ErrorMessage = "TWP API not found" };
        if (miCalculateAsync == null)
        {
            // Fall back to synchronous wrapper
            return CalculateTransfer(req);
        }

        // Build request object like in sync case
        var requestObj = Activator.CreateInstance(nestedRequestType);
        void SetIfExists(string name, object value)
        {
            var pi = nestedRequestType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (pi != null && pi.CanWrite) pi.SetValue(requestObj, Convert.ChangeType(value, pi.PropertyType), null);
        }

        SetIfExists("OriginBodyName", req.OriginBodyName);
        SetIfExists("DestinationBodyName", req.DestinationBodyName);
        SetIfExists("DepartureUT", req.DepartureUT);
        SetIfExists("TimeOfFlightUT", req.TimeOfFlightUT);
        SetIfExists("InitialOrbitAltitude", req.InitialOrbitAltitude);
        SetIfExists("InitialOrbitInclinationRad", req.InitialOrbitInclinationRad);
        SetIfExists("FinalOrbitAltitude", req.FinalOrbitAltitude);

        // Invoke async method; it returns a Task<TransferResult>
        var taskObj = miCalculateAsync.Invoke(null, new object[] { requestObj }) as Task;
        if (taskObj == null)
        {
            // fallback
            return CalculateTransfer(req);
        }

        await taskObj.ConfigureAwait(false);

        // get Result property
        var resultProperty = taskObj.GetType().GetProperty("Result");
        if (resultProperty == null) return new TransferResultDTO { Success = false, ErrorMessage = "Async task had no Result" };
        var resultObj = resultProperty.GetValue(taskObj, null);
        if (resultObj == null) return new TransferResultDTO { Success = false, ErrorMessage = "Async calculate returned null" };

        return MapResult(resultObj);
    }

    static TransferResultDTO MapResult(object resultObj)
    {
        var res = new TransferResultDTO();
        var rType = resultObj.GetType();

        bool TryGet<T>(string name, out T val)
        {
            val = default(T);
            var pi = rType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null) return false;
            var v = pi.GetValue(resultObj, null);
            if (v == null) return false;
            try { val = (T)Convert.ChangeType(v, typeof(T)); return true; } catch { return false; }
        }

        TryGet<bool>("Success", out bool b); res.Success = b;
        TryGet<string>("ErrorMessage", out string err); res.ErrorMessage = err;
        TryGet<double>("TotalDeltaV", out double tdv); res.TotalDeltaV = tdv;
        TryGet<double>("DepartureUT", out double dut); res.DepartureUT = dut;
        TryGet<double>("TimeOfFlightUT", out double tof); res.TimeOfFlightUT = tof;
        TryGet<double>("PhaseAngleRad", out double ph); res.PhaseAngleRad = ph;
        TryGet<double>("SuggestedAlarmUT", out double sau); res.SuggestedAlarmUT = sau;

        // Map vectors if present
        var piEject = rType.GetProperty("EjectionVector", BindingFlags.Public | BindingFlags.Instance);
        if (piEject != null)
        {
            var ev = piEject.GetValue(resultObj, null);
            if (ev != null)
            {
                var et = ev.GetType();
                var fx = et.GetField("x") ?? (MemberInfo)et.GetProperty("x");
                var fy = et.GetField("y") ?? (MemberInfo)et.GetProperty("y");
                var fz = et.GetField("z") ?? (MemberInfo)et.GetProperty("z");
                try
                {
                    double x = GetMemberDouble(ev, fx);
                    double y = GetMemberDouble(ev, fy);
                    double z = GetMemberDouble(ev, fz);
                    res.EjectionVector = new Vec3dDTO { x = x, y = y, z = z };
                }
                catch { }
            }
        }

        var piPeri = rType.GetProperty("PeriapsisDirection", BindingFlags.Public | BindingFlags.Instance);
        if (piPeri != null)
        {
            var pv = piPeri.GetValue(resultObj, null);
            if (pv != null)
            {
                var et = pv.GetType();
                var fx = et.GetField("x") ?? (MemberInfo)et.GetProperty("x");
                var fy = et.GetField("y") ?? (MemberInfo)et.GetProperty("y");
                var fz = et.GetField("z") ?? (MemberInfo)et.GetProperty("z");
                try
                {
                    double x = GetMemberDouble(pv, fx);
                    double y = GetMemberDouble(pv, fy);
                    double z = GetMemberDouble(pv, fz);
                    res.PeriapsisDirection = new Vec3dDTO { x = x, y = y, z = z };
                }
                catch { }
            }
        }

        return res;
    }

    static double GetMemberDouble(object obj, MemberInfo mi)
    {
        if (mi == null) throw new ArgumentNullException(nameof(mi));
        if (mi is FieldInfo fi)
        {
            var val = fi.GetValue(obj);
            return Convert.ToDouble(val);
        }
        else if (mi is PropertyInfo pi)
        {
            var val = pi.GetValue(obj, null);
            return Convert.ToDouble(val);
        }
        throw new InvalidOperationException("Unsupported member type");
    }

    public static KACAlarmSpecDTO BuildKACAlarmForTransfer(TransferResultDTO tr, int leadSeconds = 0, string alarmName = null, string notes = null)
    {
        if (!EnsureLoaded()) return null;
        if (miBuildAlarm == null) return null;

        // We need to map back to the TransferResult type expected by the API BuildKACAlarmForTransfer method.
        // Instead of reconstructing the full TransferResult nested type, we'll attempt to call BuildKACAlarmForTransfer with the original TransferResult object if it's available.
        // But since callers may only have DTO, we can build a minimal proxy object of the nested TransferResult type.

        var transferResultType = nestedResultType;
        if (transferResultType == null) return null;

        var resObj = Activator.CreateInstance(transferResultType);
        void SetIfExists(string name, object value)
        {
            var pi = transferResultType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (pi != null && pi.CanWrite) pi.SetValue(resObj, Convert.ChangeType(value, pi.PropertyType), null);
        }

        SetIfExists("Success", tr.Success);
        SetIfExists("ErrorMessage", tr.ErrorMessage);
        SetIfExists("TotalDeltaV", tr.TotalDeltaV);
        SetIfExists("DepartureUT", tr.DepartureUT);
        SetIfExists("TimeOfFlightUT", tr.TimeOfFlightUT);
        SetIfExists("PhaseAngleRad", tr.PhaseAngleRad);
        SetIfExists("SuggestedAlarmUT", tr.SuggestedAlarmUT);

        // vectors
        var piEject = transferResultType.GetProperty("EjectionVector", BindingFlags.Public | BindingFlags.Instance);
        if (piEject != null && tr.EjectionVector != null)
        {
            var vType = piEject.PropertyType;
            var vObj = Activator.CreateInstance(vType);
            SetFieldOrProp(vObj, "x", tr.EjectionVector.x);
            SetFieldOrProp(vObj, "y", tr.EjectionVector.y);
            SetFieldOrProp(vObj, "z", tr.EjectionVector.z);
            piEject.SetValue(resObj, vObj, null);
        }

        var resultAlarmObj = miBuildAlarm.Invoke(null, new object[] { resObj, leadSeconds, alarmName, notes });
        if (resultAlarmObj == null) return null;

        // Map the returned KACAlarmSpec (nested type) to DTO
        var atype = resultAlarmObj.GetType();
        var dto = new KACAlarmSpecDTO();
        var aName = atype.GetProperty("Name"); if (aName != null) dto.Name = aName.GetValue(resultAlarmObj, null) as string;
        var aUT = atype.GetProperty("AlarmUT"); if (aUT != null) dto.AlarmUT = Convert.ToDouble(aUT.GetValue(resultAlarmObj, null));
        var aLead = atype.GetProperty("LeadSeconds"); if (aLead != null) dto.LeadSeconds = Convert.ToInt32(aLead.GetValue(resultAlarmObj, null));
        var aNotes = atype.GetProperty("Notes"); if (aNotes != null) dto.Notes = aNotes.GetValue(resultAlarmObj, null) as string;
        return dto;
    }

    static void SetFieldOrProp(object obj, string name, object value)
    {
        var t = obj.GetType();
        var fi = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (fi != null) { fi.SetValue(obj, Convert.ChangeType(value, fi.FieldType)); return; }
        var pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (pi != null && pi.CanWrite) { pi.SetValue(obj, Convert.ChangeType(value, pi.PropertyType), null); return; }
    }

    public static bool TryRegisterKACAlarm(KACAlarmSpecDTO spec)
    {
        if (!EnsureLoaded()) return false;
        if (miTryRegister == null) return false;

        // Build the KACAlarmSpec nested type instance
        var kacAlarmType = miTryRegister.GetParameters().FirstOrDefault()?.ParameterType;
        object kacObj = null;
        if (kacAlarmType != null)
        {
            kacObj = Activator.CreateInstance(kacAlarmType);
            var p = kacAlarmType.GetProperty("Name"); if (p != null && p.CanWrite) p.SetValue(kacObj, spec.Name, null);
            var pu = kacAlarmType.GetProperty("AlarmUT"); if (pu != null && pu.CanWrite) pu.SetValue(kacObj, Convert.ChangeType(spec.AlarmUT, pu.PropertyType), null);
            var pl = kacAlarmType.GetProperty("LeadSeconds"); if (pl != null && pl.CanWrite) pl.SetValue(kacObj, Convert.ChangeType(spec.LeadSeconds, pl.PropertyType), null);
            var pn = kacAlarmType.GetProperty("Notes"); if (pn != null && pn.CanWrite) pn.SetValue(kacObj, spec.Notes, null);

            // Try invoking
            var result = miTryRegister.Invoke(null, new object[] { kacObj });
            if (result is bool b) return b;
            return result != null;
        }

        return false;
    }
}
