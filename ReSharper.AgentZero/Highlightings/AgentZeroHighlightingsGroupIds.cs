using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Plugin.AgentZero.Highlightings;

[assembly: RegisterConfigurableHighlightingsGroup(
  Key: AgentZeroHighlightingsGroupIds.ID,
  Title: "[Agent Zero Plugin] Satisfability issues")]

namespace JetBrains.ReSharper.Plugin.AgentZero.Highlightings
{
  public static class AgentZeroHighlightingsGroupIds
  {
    public const string ID = "SatisfabilityIssues";
  }
}