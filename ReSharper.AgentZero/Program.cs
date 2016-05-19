using System;
using Microsoft.Z3;

namespace JetBrains.ReSharper.Plugin.AgentZero
{
  public class Program
  {
    static void Main(string[] args)
    {
      var other = new Other();
      other.M();
    }
  }

  public class Other
  {
    public Status M()
    {
      using (var context = new Context())
      {
        var variable = (BitVecExpr) context.MkConst("x", context.MkBitVecSort(32));

        var solver = context.MkSolver();

        var num42 = context.MkBV(v: 42, size: 32);
        var num43 = context.MkBV(v: 43, size: 32);

        solver.Assert(context.MkBVSGT(variable, num42));
        solver.Assert(context.MkBVSLT(variable, num43));

        //context.MkReal();

        return solver.Check();

        //Console.WriteLine(status);
      }
    }
  }
}
