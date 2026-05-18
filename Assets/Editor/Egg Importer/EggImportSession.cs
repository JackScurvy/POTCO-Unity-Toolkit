using System;

public static class EggImportSession
{
    private static int explicitImportDepth;

    public static bool IsExplicitImportActive => explicitImportDepth > 0;

    public static IDisposable BeginExplicitImport()
    {
        explicitImportDepth++;
        return new ExplicitImportScope();
    }

    private sealed class ExplicitImportScope : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (explicitImportDepth > 0)
            {
                explicitImportDepth--;
            }
        }
    }
}
