namespace BS.Data;

public class AccountClassification
{
    public int Count { get; set; }
    public Dictionary<CategoryEnum, int> CategoryAmounts { get; set; }
}