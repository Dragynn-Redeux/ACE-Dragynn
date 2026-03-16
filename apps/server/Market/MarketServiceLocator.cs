using System;
using ACE.Server.Managers;

namespace ACE.Server.Market;

/// <summary>
/// Simple static access point for market-related services.
/// </summary>
public static class MarketServiceLocator
{
    public static int CalculateSaleFee(int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var saleFee = PropertyManager.GetDouble("market_listing_payout_fee").Item;

        return (int)Math.Floor(amount * saleFee);
    }

    public static int CalculateFee(int amount) => CalculateSaleFee(amount);

    public static int CalculateCancellationFee(int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var cancellationFee = PropertyManager.GetDouble("market_listing_cancellation_fee").Item;

        return (int)Math.Floor(amount * cancellationFee);
    }

    public static int CalculateNetAfterFee(int amount)
    {
        return Math.Max(0, amount - CalculateSaleFee(amount));
    }

    private static IPlayerMarketRepository? _playerMarketRepository;
    private static ACE.Server.Market.PlayerMarketConfig? _config;  // fully qualified

    /// <summary>
    /// Market configuration (listing lifetime, limits, etc.).
    /// </summary>
    public static ACE.Server.Market.PlayerMarketConfig Config =>   // fully qualified
        _config ?? throw new InvalidOperationException(
            "MarketServiceLocator.Config has not been initialized."
        );

    /// <summary>
    /// Call this once during server startup to wire up the market subsystem.
    /// </summary>
    public static void Initialize(
        IPlayerMarketRepository playerMarketRepository,
        ACE.Server.Market.PlayerMarketConfig? config = null)       // fully qualified
    {
        _playerMarketRepository =
            playerMarketRepository ?? throw new ArgumentNullException(nameof(playerMarketRepository));

        _config = config ?? new ACE.Server.Market.PlayerMarketConfig();
    }

    /// <summary>
    /// Repository used for all persistence of listings and payouts.
    /// Must be initialized during server startup.
    /// </summary>
    public static IPlayerMarketRepository PlayerMarketRepository =>
        _playerMarketRepository ?? throw new InvalidOperationException(
            "MarketServiceLocator.PlayerMarketRepository has not been initialized."
        );
}
