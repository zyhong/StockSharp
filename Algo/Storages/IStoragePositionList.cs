#region S# License
/******************************************************************************************
NOTICE!!!  This program and source code is owned and licensed by
StockSharp, LLC, www.stocksharp.com
Viewing or use of this code requires your acceptance of the license
agreement found at https://github.com/StockSharp/StockSharp/blob/master/LICENSE
Removal of this comment is a violation of the license agreement.

Project: StockSharp.Algo.Storages.Algo
File: IStoragePositionList.cs
Created: 2015, 12, 2, 8:18 PM

Copyright 2010 by StockSharp, LLC
*******************************************************************************************/
#endregion S# License
namespace StockSharp.Algo.Storages
{
	using Ecng.Collections;

	using StockSharp.BusinessEntities;
	using StockSharp.Messages;

	/// <summary>
	/// The interface for access to the position storage.
	/// </summary>
	public interface IStoragePositionList : ICollectionEx<Position>, IStorageEntityList<Position>
	{
		/// <summary>
		/// To get the position by portfolio and instrument.
		/// </summary>
		/// <param name="portfolio">The portfolio on which the position should be found.</param>
		/// <param name="security">The instrument on which the position should be found.</param>
		/// <param name="strategyId">Strategy ID.</param>
		/// <param name="clientCode">The client code.</param>
		/// <param name="depoName">The depository name where the stock is located physically. By default, an empty string is passed, which means the total position by all depositories.</param>
		/// <param name="limit">Limit type for �+ market.</param>
		/// <returns>Position.</returns>
		Position GetPosition(Portfolio portfolio, Security security, string strategyId, string clientCode = "", string depoName = "", TPlusLimits? limit = null);
	}
}