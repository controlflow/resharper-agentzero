using System;
using System.Globalization;
using System.Text;
using JetBrains.Annotations;
using JetBrains.ReSharper.Daemon.Stages.Dispatcher;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Plugin.AgentZero.Highlightings;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using Microsoft.Z3;

namespace JetBrains.ReSharper.Plugin.AgentZero.Analysis
{
  [ElementProblemAnalyzer(
    ElementTypes: new[] { typeof(ICSharpExpression) },
    HighlightingTypes = new[] { typeof(UnsatisfiableExpressionError) })]
  public class SatisfabilityProblemAnalyzer : ElementProblemAnalyzer<ICSharpExpression>
  {
    protected override void Run(ICSharpExpression element, ElementProblemAnalyzerData data, IHighlightingConsumer consumer)
    {
      var binaryExpression = element as IBinaryExpression;
      if (binaryExpression == null) return;

      var expressionType = binaryExpression.Type();
      if (!expressionType.IsBool()) return;

      using (var context = new Context())
      {
        var expressionsRewriter = new ExpressionsRewriter(context, element.GetPredefinedType());

        var expr = binaryExpression.Accept(expressionsRewriter, null) as BoolExpr;
        if (expr != null)
        {
          var solver = context.MkSolver();
          solver.Assert(expr);

          var status = solver.Check();
          if (status == Status.UNSATISFIABLE)
          {
            consumer.AddHighlighting(new UnsatisfiableExpressionError(binaryExpression));
          }
          else if (status == Status.SATISFIABLE)
          {
            var modelPresentation = ModelPresenter(solver.Model);

            consumer.AddHighlighting(new SatisfiableExpressionHint(binaryExpression, modelPresentation));
          }
        }
      }
    }

    [NotNull]
    private string ModelPresenter([NotNull] Model model)
    {
      var builder = new StringBuilder();

      if (model.NumConsts > 0)
      {
        builder.AppendLine("CONSTANTS:");


        foreach (FuncDecl funcDecl in model.ConstDecls)
        {
          builder.Append(funcDecl.Name).Append(" = ");
          //builder.Append(funcDecl.SExpr());

          var expr = model.Eval(funcDecl.Apply());
          if (expr.IsBV)
          {
            builder.Append(expr.ToString());
          }
          else if (expr.IsFPNumeral)
          {
            if (expr.IsFPPlusInfinity)
            {
              builder.Append("+oo");
            }
            else if (expr.IsFPMinusInfinity)
            {
              builder.Append("-oo");
            }
            else if (expr.IsFPMinusZero)
            {
              builder.Append("-0");
            }
            else if (expr.IsFPNaN)
            {
              builder.Append("NaN");
            }
            else
            {
              // todo: check sign
              var num = (FPNum) expr;
              var parsed = double.Parse(num.Significand + "E" + num.Exponent, NumberStyles.AllowExponent);
              builder.Append(parsed.ToString(CultureInfo.InvariantCulture));
            }
          }
          else if (expr.IsBool)
          {
            builder.Append(expr.IsTrue.ToString());
          }
          else
          {
            builder.Append(expr.SExpr());
          }

          builder.AppendLine();
        }

        builder.AppendLine();
      }

      builder.AppendLine("MODEL:");
      builder.Append(model.ToString());

      return builder.ToString();
    }

    // todo: can process visitor
    // todo: process visitor
  }

  // todo: support bool types, sbyte/byte/char/ushort/short/int/uint/long/ulong, what about decimal/float?
  // todo: enums are integers
  // todo: Enum.* members to support: HasFlag, maybe IsDefined?
  // todo: check enums if they are closed
}