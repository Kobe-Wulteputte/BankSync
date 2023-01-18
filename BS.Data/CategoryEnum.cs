using System.Runtime.Serialization;

namespace BS.Data;

public enum CategoryEnum
{
    Clothes,
    Communication,
    Education,

    [EnumMember(Value = "Food and drink (other)")]
    FoodAndDrink,
    Groceries,
    Health,
    Home,
    Activities,
    Chiro,
    Cycling,
    Sports,
    Subscriptions,
    Takeaway,
    Transport,
    [EnumMember(Value = "Fast food")] FastFood,
    Gifts,
    Donations,
    Drinks,
    Gadgets,
    Games,
    Restaurants,
    Shows,
    Travel,
    [EnumMember(Value = "Bank services")] BankServices,
    Fines,
    Cash,
    Investments,
    Transfer,
    Vouchers,
    Bonus,
    Wage,
    Taxes,
    Reimbursement
}