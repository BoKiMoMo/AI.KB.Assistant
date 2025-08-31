namespace AI.KB.Assistant.Models;

public class AppConfig
{
	public AppSection App { get; set; } = new();
	public ClassificationSection Classification { get; set; } = new();
	public TimePolicySection TimePolicy { get; set; } = new();
	public RoutingSection Routing { get; set; } = new();
	public OpenAISection OpenAI { get; set; } = new();
}

public record AppSection(
	string RootDir = "",
	string InboxDir = "",
	string DbPath = "",
	bool DryRun = true,
	string MoveMode = "move",      // move | copy
	string Overwrite = "rename"    // skip | rename | overwrite
);

public record ClassificationSection(
	string Engine = "dummy",       // dummy | llm | hybrid
	string Style = "topic",
	double ConfidenceThreshold = 0.72,
	string FallbackCategory = "unsorted",
	List<string>? CustomTaxonomy = null
);

public record TimePolicySection(
	string Source = "auto",        // created | modified | parsed | auto
	List<string>? FilenameDatePatterns = null,
	bool ContentDateHint = true
);

public record RoutingSection(
	string PathTemplate = "{root}/{category}/{yyyy}/{mm}/",
	bool SafeCategories = true
);

public record OpenAISection(
	string ApiKey = "",
	string Model = "gpt-4o-mini"
);
