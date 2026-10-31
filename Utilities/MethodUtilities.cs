using System;

namespace TriggerTurn.Utilities;

/// <summary>
/// Utilities for working with methods.
/// </summary>
public static class MethodUtilities
{
    /// <summary>
    /// Returns true if the provided Action caused no errors in execution, and includes the Exception if failed.
    /// </summary>
    public static (bool, Exception?) Attempt(Action method)
    {
        try { method(); }
        catch (Exception ex)
        {
            return (false, ex);
        }

        return (true, null);
    }
}