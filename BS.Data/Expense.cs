namespace BS.Data;

public class Expense
{
    public string Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string Account { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }
    public string Group { get; set; }
    public bool Reimbursed { get; set; }
    public string Description { get; set; }
    public string Id { get; set; }

    public string AiPrompt =>
        $"Type: {Type}, Amount: {Amount}, Date: {Date:dd-MM-yyyy}, Name: {Name}, Description: {Description} ->";
}