using System;
using ACE.Server.Managers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class BankActivityLogTests
{
    [TestMethod]
    public void DescribeItem_ShowsAmountsOnlyForStacks()
    {
        Assert.AreEqual("Pyreal (5,000)", BankActivityLog.DescribeItem("Pyreal", 5000));
        Assert.AreEqual("Iron Jitte", BankActivityLog.DescribeItem("Iron Jitte", null));
    }

    [TestMethod]
    public void DescribeItems_ListsWhatFitsAndCountsTheRest()
    {
        Assert.AreEqual("", BankActivityLog.DescribeItems([]));
        Assert.AreEqual("Iron Jitte", BankActivityLog.DescribeItems(["Iron Jitte"]));
        Assert.AreEqual("2 items: Iron Jitte, Pyreal (5,000)", BankActivityLog.DescribeItems(["Iron Jitte", "Pyreal (5,000)"]));

        var many = new string[30];
        for (var i = 0; i < many.Length; i++)
        {
            many[i] = $"Long Sword {i}";
        }

        var text = BankActivityLog.DescribeItems(many, 80);

        Assert.IsTrue(text.Length <= 80, text);
        Assert.IsTrue(text.StartsWith("30 items: Long Sword 0, "), text);
        Assert.IsTrue(text.EndsWith(" more"), text);
    }

    [TestMethod]
    public void DescribeAge_RoundsDownToTheLargestUnit()
    {
        Assert.AreEqual("just now", BankActivityLog.DescribeAge(TimeSpan.FromSeconds(40)));
        Assert.AreEqual("5m ago", BankActivityLog.DescribeAge(TimeSpan.FromMinutes(5.9)));
        Assert.AreEqual("3h ago", BankActivityLog.DescribeAge(TimeSpan.FromHours(3.2)));
        Assert.AreEqual("2d ago", BankActivityLog.DescribeAge(TimeSpan.FromDays(2.5)));
    }
}
