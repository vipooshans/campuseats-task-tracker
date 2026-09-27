namespace LankaMart.ServiceTests;

// ============================================================
//  A 40-line stand-in for a test framework.
//
//  A real project uses xUnit:   dotnet new xunit
//  This runner exists only so the demo has ZERO external
//  packages and runs anywhere, including an exam-hall machine
//  with no internet. The IDEA being demonstrated is identical.
// ============================================================
public static class MiniTest
{
    private static int _passed;
    private static int _failed;

    public static async Task Run(string name, Func<Task> body)
    {
        try
        {
            await body();
            _passed++;
            Console.WriteLine($"  PASS  {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        {ex.Message}");
        }
    }

    public static void AreEqual<T>(T expected, T actual, string because)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}, got {actual}. {because}");
    }

    public static void IsTrue(bool condition, string because)
    {
        if (!condition) throw new Exception($"Expected true. {because}");
    }

    public static async Task Throws<TException>(Func<Task> action, string because)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;     // the expected failure
        }
        catch (Exception ex)
        {
            throw new Exception($"Expected {typeof(TException).Name}, got {ex.GetType().Name}. {because}");
        }

        throw new Exception($"Expected {typeof(TException).Name}, but nothing was thrown. {because}");
    }

    public static int Summary()
    {
        Console.WriteLine();
        Console.WriteLine($"  {_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }
}
