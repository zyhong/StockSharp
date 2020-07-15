namespace StockSharp.BusinessEntities
{
	using System;
	using System.Collections.Generic;

	using StockSharp.Messages;

	/// <summary>
	/// The position provider interface.
	/// </summary>
	public interface IPositionProvider : IPortfolioProvider
	{
		/// <summary>
		/// Get all positions.
		/// </summary>
		IEnumerable<Position> Positions { get; }

		/// <summary>
		/// New position received.
		/// </summary>
		event Action<Position> NewPosition;

		/// <summary>
		/// Position changed.
		/// </summary>
		event Action<Position> PositionChanged;

		/// <summary>
		/// To get the position by portfolio and instrument.
		/// </summary>
		/// <param name="portfolio">The portfolio on which the position should be found.</param>
		/// <param name="security">The instrument on which the position should be found.</param>
		/// <param name="strategyId">Strategy ID.</param>
		/// <param name="clientCode">The client code.</param>
		/// <param name="depoName">The depository name where the stock is located physically. By default, an empty string is passed, which means the total position by all depositories.</param>
		/// <param name="limitType">Limit type for �+ market.</param>
		/// <returns>Position.</returns>
		Position GetPosition(Portfolio portfolio, Security security, string strategyId, string clientCode = "", string depoName = "", TPlusLimits? limitType = null);
	}
}