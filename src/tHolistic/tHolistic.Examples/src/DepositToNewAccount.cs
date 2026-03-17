using System.Diagnostics;
using Bogus;

namespace _42.tHolistic.Examples;

public class DepositToNewAccount
{
    public static UserAccountDepositModel[] Models = new[]
    {
        new UserAccountDepositModel { UserName = "John Doe" },
        new UserAccountDepositModel { UserName = "Jane Doe" },
    };

    private Faker _faker = new();

    [Test]
    public void Test(UserAccountDepositModel model)
    {
        CreateUser(model);
        CreateAccount(model);
        DepositMoney(model);
    }

    [Step]
    public void CreateUser(IUserModel model)
    {
        Console.WriteLine($"User {model.UserName} created.");
        Debug.WriteLine($"User {model.UserName} created.");
    }

    [Step]
    public void CreateAccount(IAccountModel model)
    {
        model.AccountNumber = _faker.Finance.Account();
        Console.WriteLine($"Account {model.AccountNumber} created.");
        Debug.WriteLine($"Account {model.AccountNumber} created.");
    }

    [Step]
    public void DepositMoney(IDepositModel model)
    {
        model.DepositAmount = _faker.Finance.Amount(100);
        Console.WriteLine($"A deposit of {model.DepositAmount} has been made.");
        Debug.WriteLine($"A deposit of {model.DepositAmount} has been made.");
    }
}

public class UserAccountDepositModel : IUserModel, IAccountModel, IDepositModel
{
    public string UserName { get; set; }

    public string AccountNumber { get; set; }

    public decimal DepositAmount { get; set; }
}

public interface IUserModel
{
    string UserName { get; set; }
}

public interface IAccountModel
{
    string AccountNumber { get; set; }
}

public interface IDepositModel
{
    decimal DepositAmount { get; set; }
}
