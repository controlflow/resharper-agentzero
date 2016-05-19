using System.Security;
using JetBrains.DataFlow;
using JetBrains.Util.Interop;

namespace JetBrains.ReSharper.Plugin.AgentZero.Z3Interop
{
  [SuppressUnmanagedCodeSecurity]
  public class Z3Interop
  {
    public const string MicrosoftZ3Dll = "libz3.dll";

    public void Init()
    {
      //var dll = NativeDllsLoader.LoadDll(EternalLifetime.Instance, MicrosoftZ3Dll);



      //dll.ImportMethod<>()
    }
  }
}
