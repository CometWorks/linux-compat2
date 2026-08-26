using System.Runtime.InteropServices;

namespace LinuxCompat.Platform;

/// <summary>
/// Immediate process termination: the Linux counterpart of the Win32 <c>ExitProcess</c> call
/// behind <c>VRageWindows.TerminateProcess</c>.
///
/// The engine asks the platform to terminate when it wants the process gone right now, without
/// unwinding: from <c>VRageCore.Terminate</c>, which is the crash handler's termination callback
/// and also what the Remote API's forced exit invokes, and from <c>RestartToReport</c>. At that
/// point nothing has been shut down — the render, SDL, worker and Steam callback threads are all
/// still running and the Steamworks API is still initialized.
///
/// <c>Environment.Exit</c> cannot serve that contract. It runs a full CLR shutdown and then hands
/// over to glibc's <c>exit()</c>, which runs the exit handlers <c>steamclient.so</c> registered.
/// Those tear Steam's networking down while its sockets are still open, which trips a Valve
/// assert (<c>Trying to close low level socket support, but we still have sockets open!</c>).
/// The assert is reported through <c>crashhandler.so</c>, which writes a minidump from a forked
/// child while the dying process holds the handler's lock. Observed outcomes ranged from a stray
/// minidump through a segfault to a permanent hang of the main thread inside
/// <c>__run_exit_handlers</c>. Windows never sees any of this, because <c>ExitProcess</c> does
/// not run the C runtime's exit handlers.
///
/// <c>_exit(2)</c> is the primitive that matches: it goes straight to <c>exit_group</c>, so no
/// exit handler, finalizer or native destructor runs and every thread dies with the process. The
/// game's own log is already flushed by the callers, and Steam notices a vanished process by
/// itself.
///
/// The graceful shutdown ("Exit to Linux" from the game's menus, or the Remote API's exit without
/// <c>force</c>) never reaches this method, which is why only the terminate path was ever
/// affected: Pulsar prefixes <c>VRageCore.Exit</c> with a <c>Process.GetCurrentProcess().Kill()</c>
/// of its own and skips the original, so that path leaves the process by <c>SIGKILL</c> — which
/// runs no exit handler either.
/// </summary>
internal static class LinuxProcessTermination
{
    /// <summary>
    /// Ends the process with <paramref name="exitCode"/> without running any exit handler.
    /// Does not return.
    /// </summary>
    public static void Terminate(int exitCode)
    {
        // stdio buffers die with the exit handlers, so anything the plugin still holds has to go
        // out first. The game's log is flushed by VRageCore.Terminate before it calls in here.
        TryFlush(Console.Out);
        TryFlush(Console.Error);

        try
        {
            _exit(exitCode);
        }
        catch (Exception exception)
        {
            // Only an unusable libc reaches this, which a running Linux process does not have.
            // Falling back is still better than returning: a process that was told to die has to.
            Console.Error.WriteLine($"[LinuxCompat] WARNING: _exit({exitCode}) failed: {exception.Message}");
            Environment.Exit(exitCode);
        }
    }

    private static void TryFlush(TextWriter writer)
    {
        try
        {
            writer.Flush();
        }
        catch (Exception)
        {
            // A console that cannot be flushed must not keep the process alive.
        }
    }

    [DllImport("libc", EntryPoint = "_exit")]
    private static extern void _exit(int status);
}
