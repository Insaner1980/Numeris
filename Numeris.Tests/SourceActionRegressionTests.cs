using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Numeris.ViewModels.Sources;

internal static class SourceActionRegressionTests
{
    public static void PreventsConcurrentActions(Type sourceType)
    {
        var source = RuntimeHelpers.GetUninitializedObject(sourceType);
        sourceType.GetProperty("IsBusy")!.SetValue(source, true);
        sourceType.GetProperty("StatusMessage")!.SetValue(source, "Operation in progress");
        var actions = sourceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name.EndsWith("Async", StringComparison.Ordinal)
                && method.Name != "LoadAsync" && method.ReturnType == typeof(Task));
        foreach (var action in actions)
        {
            var args = action.GetParameters().Select(parameter => parameter.ParameterType == typeof(string)
                ? (object)"https://test.example/" : Activator.CreateInstance(parameter.ParameterType)).ToArray();
            ((Task)action.Invoke(source, args)!).GetAwaiter().GetResult();
            if (!(bool)sourceType.GetProperty("IsBusy")!.GetValue(source)!
                || (string)sourceType.GetProperty("StatusMessage")!.GetValue(source)! != "Operation in progress")
            {
                throw new InvalidOperationException($"{action.Name} must leave the running operation unchanged");
            }
        }
    }

    public static void ReportsDeleteFailure(Type sourceType)
    {
        // Uninitialized dependencies fail locally without touching credentials or user data.
        var source = RuntimeHelpers.GetUninitializedObject(sourceType);
        var connectionProperty = sourceType.GetProperty("Connection");
        if (connectionProperty is not null)
        {
            connectionProperty.SetValue(source, Activator.CreateInstance(connectionProperty.PropertyType));
        }
        var action = sourceType.GetMethod("DeleteAsync")!;
        var args = action.GetParameters().Select(parameter => Activator.CreateInstance(parameter.ParameterType)).ToArray();
        ((Task)action.Invoke(source, args)!).GetAwaiter().GetResult();
        var status = (string)sourceType.GetProperty("StatusMessage")!.GetValue(source)!;
        if (!status.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || !(bool)sourceType.GetProperty("CanRun")!.GetValue(source)!)
        {
            throw new InvalidOperationException("Delete failure must be visible and release the busy state");
        }
    }
}
