using JetBrains.Annotations;
using JetBrains.DocumentModel;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Plugin.AgentZero.Highlightings;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.CSharp.Tree;
// ReSharper disable ArgumentsStyleNamedExpression

[assembly: RegisterConfigurableSeverity(
  ID: UnsatisfiableExpressionError.SEVERITY_ID,
  CompoundItemName: null,
  Group: AgentZeroHighlightingsGroupIds.ID,
  Title: UnsatisfiableExpressionError.MESSAGE,
  Description: "Expression is detected to be never satisfiable with any given variables values",
  DefaultSeverity: Severity.ERROR,
  SolutionAnalysisRequired: false)]

[assembly: RegisterConfigurableSeverity(
  ID: SatisfiableExpressionHint.SEVERITY_ID,
  CompoundItemName: null,
  Group: AgentZeroHighlightingsGroupIds.ID,
  Title: UnsatisfiableExpressionError.MESSAGE,
  Description: "Expression is detected to be satisfiable with given model of variables",
  DefaultSeverity: Severity.HINT,
  SolutionAnalysisRequired: false)]

namespace JetBrains.ReSharper.Plugin.AgentZero.Highlightings
{
  [ConfigurableSeverityHighlighting(
    ConfigurableSeverityId: SEVERITY_ID,
    Languages: CSharpLanguage.Name,
    ShowToolTipInStatusBar = false,
    ToolTipFormatString = MESSAGE)]
  public sealed class UnsatisfiableExpressionError : IHighlighting
  {
    public const string SEVERITY_ID = "AgentZero.UnsatisfiableExpression";
    public const string MESSAGE = "Expression is unsatisfiable";

    [NotNull] public ICSharpExpression Expression { get; private set; }

    public UnsatisfiableExpressionError([NotNull] ICSharpExpression expression)
    {
      Expression = expression;
    }

    public bool IsValid() { return Expression.IsValid(); }

    public DocumentRange CalculateRange()
    {
      return Expression.GetHighlightingRange();
    }

    [NotNull] public string ToolTip { get { return MESSAGE; } }
    [NotNull] public string ErrorStripeToolTip { get { return ToolTip; } }
  }

  // todo: always satisfiable expression
  // todo: simplified-to-constant expression?

  [ConfigurableSeverityHighlighting(
    ConfigurableSeverityId: SEVERITY_ID,
    Languages: CSharpLanguage.Name,
    ShowToolTipInStatusBar = false,
    ToolTipFormatString = MESSAGE)]
  public sealed class SatisfiableExpressionHint : IHighlighting
  {
    public const string SEVERITY_ID = "AgentZero.SatisfiableExpression";
    public const string MESSAGE = "Expression is satisfiable, model is: \r\n\r\n{0}";

    [NotNull] public ICSharpExpression Expression { get; private set; }
    [NotNull] public string ModelDump { get; private set; }

    public SatisfiableExpressionHint([NotNull] ICSharpExpression expression, [NotNull] string modelDump)
    {
      Expression = expression;
      ModelDump = modelDump;
    }

    public bool IsValid() { return Expression.IsValid(); }

    public DocumentRange CalculateRange()
    {
      return Expression.GetHighlightingRange();
    }

    [NotNull] public string ToolTip { get { return string.Format(MESSAGE, ModelDump); } }
    [NotNull] public string ErrorStripeToolTip { get { return ToolTip; } }
  }

}