using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RpgGame.Api.Services;
using RpgGame.Rules;

namespace RpgGame.Api.Tests;

public class DiceSetupTests
{
    private static IDiceRoller Create(string environment, string? seed)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(seed is null ? [] : [new KeyValuePair<string, string?>(DiceSetup.SeedKey, seed)])
            .Build();
        return DiceSetup.Create(config, new Env(environment));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void No_seed_means_random_dice(string environment) =>
        Assert.IsType<RandomDice>(Create(environment, null));

    [Theory]
    [InlineData("Development")]
    [InlineData("E2E")]
    public void A_seed_gives_round_seeded_dice_in_development_and_e2e(string environment)
    {
        var dice = Assert.IsType<RoundSeededDice>(Create(environment, "7"));
        Assert.Equal(7, dice.Seed);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void A_seed_anywhere_else_stops_the_api(string environment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Create(environment, "7"));
        Assert.Contains("only allowed in Development and E2E", ex.Message);
    }

    [Fact]
    public void A_seed_that_isnt_a_number_stops_the_api() =>
        Assert.Throws<InvalidOperationException>(() => Create("E2E", "lucky"));

    [Fact]
    public void Each_round_rolls_the_same_however_much_was_rolled_before()
    {
        var dice = new RoundSeededDice(1);
        static int[] Rolls(IDiceRoller d) => Enumerable.Range(0, 10).Select(_ => d.RollD20()).ToArray();

        var first = Rolls(dice.ForRound(3));
        Rolls(dice.ForRound(1));
        Rolls(dice);
        Assert.Equal(first, Rolls(dice.ForRound(3)));
        Assert.NotEqual(first, Rolls(dice.ForRound(4)));
        Assert.NotEqual(first, Rolls(new RoundSeededDice(2).ForRound(3)));
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "RpgGame.Api";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
