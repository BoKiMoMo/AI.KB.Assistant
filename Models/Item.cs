namespace AI.KB.Assistant.Models;

public class Item
{
	public long Id { get; set; }
	public string Path { get; set; } = "";
	public string Filename { get; set; } = "";
	public string? Category { get; set; }
	public string Status { get; set; } = "To-Do";
	public double Confidence { get; set; }
	public long CreatedTs { get; set; }   // epoch seconds
	public string? Summary { get; set; }
	public string? Reasoning { get; set; }
}
