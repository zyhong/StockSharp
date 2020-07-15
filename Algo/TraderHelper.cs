#region S# License
/******************************************************************************************
NOTICE!!!  This program and source code is owned and licensed by
StockSharp, LLC, www.stocksharp.com
Viewing or use of this code requires your acceptance of the license
agreement found at https://github.com/StockSharp/StockSharp/blob/master/LICENSE
Removal of this comment is a violation of the license agreement.

Project: StockSharp.Algo.Algo
File: TraderHelper.cs
Created: 2015, 11, 11, 2:32 PM

Copyright 2010 by StockSharp, LLC
*******************************************************************************************/
#endregion S# License
namespace StockSharp.Algo
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Linq;

	using Ecng.Common;
	using Ecng.Collections;
	using Ecng.ComponentModel.Expressions;

	using MoreLinq;

	using StockSharp.Algo.Storages;
	using StockSharp.Algo.Testing;
	using StockSharp.BusinessEntities;
	using StockSharp.Logging;
	using StockSharp.Messages;
	using StockSharp.Localization;

	/// <summary>
	/// Price rounding rules.
	/// </summary>
	public enum ShrinkRules
	{
		/// <summary>
		/// Automatically to determine rounding to lesser or to bigger value.
		/// </summary>
		Auto,

		/// <summary>
		/// To round to lesser value.
		/// </summary>
		Less,

		/// <summary>
		/// To round to bigger value.
		/// </summary>
		More,
	}

	/// <summary>
	/// The auxiliary class for provision of various algorithmic functionalities.
	/// </summary>
	public static partial class TraderHelper
	{
		private static readonly bool[][] _stateChangePossibilities;

		static TraderHelper()
		{
			_stateChangePossibilities = new bool[5][];

			for (var i = 0; i < _stateChangePossibilities.Length; i++)
				_stateChangePossibilities[i] = new bool[_stateChangePossibilities.Length];

			_stateChangePossibilities[(int)OrderStates.None][(int)OrderStates.None] = true;
			_stateChangePossibilities[(int)OrderStates.None][(int)OrderStates.Pending] = true;
			_stateChangePossibilities[(int)OrderStates.None][(int)OrderStates.Active] = true;
			_stateChangePossibilities[(int)OrderStates.None][(int)OrderStates.Done] = true;
			_stateChangePossibilities[(int)OrderStates.None][(int)OrderStates.Failed] = true;

			_stateChangePossibilities[(int)OrderStates.Pending][(int)OrderStates.None] = false;
			_stateChangePossibilities[(int)OrderStates.Pending][(int)OrderStates.Pending] = true;
			_stateChangePossibilities[(int)OrderStates.Pending][(int)OrderStates.Active] = true;
			//_stateChangePossibilities[(int)OrderStates.Pending][(int)OrderStates.Done] = true;
			_stateChangePossibilities[(int)OrderStates.Pending][(int)OrderStates.Failed] = true;

			_stateChangePossibilities[(int)OrderStates.Active][(int)OrderStates.None] = false;
			_stateChangePossibilities[(int)OrderStates.Active][(int)OrderStates.Pending] = false;
			_stateChangePossibilities[(int)OrderStates.Active][(int)OrderStates.Active] = true;
			_stateChangePossibilities[(int)OrderStates.Active][(int)OrderStates.Done] = true;
			_stateChangePossibilities[(int)OrderStates.Active][(int)OrderStates.Failed] = false;

			_stateChangePossibilities[(int)OrderStates.Done][(int)OrderStates.None] = false;
			_stateChangePossibilities[(int)OrderStates.Done][(int)OrderStates.Pending] = false;
			_stateChangePossibilities[(int)OrderStates.Done][(int)OrderStates.Active] = false;
			_stateChangePossibilities[(int)OrderStates.Done][(int)OrderStates.Done] = true;
			_stateChangePossibilities[(int)OrderStates.Done][(int)OrderStates.Failed] = false;

			_stateChangePossibilities[(int)OrderStates.Failed][(int)OrderStates.None] = false;
			_stateChangePossibilities[(int)OrderStates.Failed][(int)OrderStates.Pending] = false;
			_stateChangePossibilities[(int)OrderStates.Failed][(int)OrderStates.Active] = false;
			_stateChangePossibilities[(int)OrderStates.Failed][(int)OrderStates.Done] = false;
			_stateChangePossibilities[(int)OrderStates.Failed][(int)OrderStates.Failed] = true;

			UsdRateMinAvailableTime = new DateTime(2009, 11, 2);
		}

		/// <summary>
		/// Check the possibility order's state change.
		/// </summary>
		/// <param name="order">Order.</param>
		/// <param name="state">Current order's state.</param>
		/// <param name="logs">Logs.</param>
		public static void ApplyNewState(this Order order, OrderStates state, ILogReceiver logs = null)
		{
			order.State = ((OrderStates?)order.State).ApplyNewState(state, order.TransactionId, logs);
		}

		/// <summary>
		/// Check the possibility <see cref="Order.State"/> change.
		/// </summary>
		/// <param name="currState">Current order's state.</param>
		/// <param name="newState">New state.</param>
		/// <param name="transactionId">Transaction id.</param>
		/// <param name="logs">Logs.</param>
		/// <returns>New state.</returns>
		public static OrderStates ApplyNewState(this OrderStates? currState, OrderStates newState, long transactionId, ILogReceiver logs = null)
		{
			if (logs != null && currState != null && !_stateChangePossibilities[(int)currState.Value][(int)newState])
				logs.AddWarningLog($"Order {transactionId} invalid state change: {currState} -> {newState}");

			return newState;
		}

		/// <summary>
		/// Check the possibility <see cref="Order.Balance"/> change.
		/// </summary>
		/// <param name="currBal">Current balance.</param>
		/// <param name="newBal">New balance.</param>
		/// <param name="transactionId">Transaction id.</param>
		/// <param name="logs">Logs.</param>
		/// <returns>New balance.</returns>
		public static decimal ApplyNewBalance(this decimal? currBal, decimal newBal, long transactionId, ILogReceiver logs)
		{
			if (logs is null)
				throw new ArgumentNullException(nameof(logs));

			if (newBal < 0)
				logs.AddErrorLog($"Order {transactionId}: balance {newBal} < 0");

			if (currBal < newBal)
				logs.AddErrorLog($"Order {transactionId}: bal_old {currBal} -> bal_new {newBal}");

			return newBal;
		}

		/// <summary>
		/// To calculate the current price by the instrument depending on the order direction.
		/// </summary>
		/// <param name="security">The instrument used for the current price calculation.</param>
		/// <param name="provider">The market data provider.</param>
		/// <param name="direction">Order side.</param>
		/// <param name="priceType">The type of market price.</param>
		/// <param name="orders">Orders to be ignored.</param>
		/// <returns>The current price. If information in order book is insufficient, then <see langword="null" /> will be returned.</returns>
		public static Unit GetCurrentPrice(this Security security, IMarketDataProvider provider, Sides? direction = null, MarketPriceTypes priceType = MarketPriceTypes.Following, IEnumerable<Order> orders = null)
		{
			if (provider == null)
				throw new ArgumentNullException(nameof(provider));

			decimal? currentPrice = null;

			if (direction != null)
			{
				currentPrice = (decimal?)provider.GetSecurityValue(security,
					direction == Sides.Buy ? Level1Fields.BestAskPrice : Level1Fields.BestBidPrice);
			}

			if (currentPrice == null)
				currentPrice = (decimal?)provider.GetSecurityValue(security, Level1Fields.LastTradePrice);

			if (currentPrice == null)
				currentPrice = 0;

			return new Unit((decimal)currentPrice).SetSecurity(security);
		}

		/// <summary>
		/// To calculate the current price by the order book depending on the order direction.
		/// </summary>
		/// <param name="depth">The order book for the current price calculation.</param>
		/// <param name="side">The order direction. If it is a buy, <see cref="MarketDepth.BestAsk"/> value is used, otherwise <see cref="MarketDepth.BestBid"/>.</param>
		/// <param name="priceType">The type of current price.</param>
		/// <param name="orders">Orders to be ignored.</param>
		/// <returns>The current price. If information in order book is insufficient, then <see langword="null" /> will be returned.</returns>
		/// <remarks>
		/// For correct operation of the method the order book export shall be launched.
		/// </remarks>
		public static Unit GetCurrentPrice(this MarketDepth depth, Sides side, MarketPriceTypes priceType = MarketPriceTypes.Following, IEnumerable<Order> orders = null)
		{
			if (depth == null)
				throw new ArgumentNullException(nameof(depth));

			if (orders != null)
			{
				var dict = new Dictionary<Tuple<Sides, decimal>, HashSet<Order>>();

				foreach (var order in orders)
				{
					if (!dict.SafeAdd(Tuple.Create(order.Direction, order.Price)).Add(order))
						throw new InvalidOperationException(LocalizedStrings.Str415Params.Put(order));
				}

				var bids = depth.Bids2.ToList();
				var asks = depth.Asks2.ToList();

				for (var i = 0; i < bids.Count; i++)
				{
					var quote = bids[i];

					if (dict.TryGetValue(Tuple.Create(Sides.Buy, quote.Price), out var bidOrders))
					{
						foreach (var order in bidOrders)
						{
							if (!orders.Contains(order))
								quote.Volume -= order.Balance;
						}

						if (quote.Volume <= 0)
						{
							bids.RemoveAt(i);
							i--;
						}
						else
							bids[i] = quote;
					}
				}

				for (var i = 0; i < asks.Count; i++)
				{
					var quote = asks[i];

					if (dict.TryGetValue(Tuple.Create(Sides.Sell, quote.Price), out var asksOrders))
					{
						foreach (var order in asksOrders)
						{
							if (!orders.Contains(order))
								quote.Volume -= order.Balance;
						}

						if (quote.Volume <= 0)
						{
							asks.RemoveAt(i);
							i--;
						}
						else
							asks[i] = quote;
					}
				}

				depth = new MarketDepth(depth.Security).Update(bids.ToArray(), asks.ToArray(), depth.LastChangeTime);
			}

			var pair = depth.BestPair;
			return pair?.GetCurrentPrice(side, priceType);
		}

		/// <summary>
		/// To calculate the current price based on the best pair of quotes, depending on the order direction.
		/// </summary>
		/// <param name="bestPair">The best pair of quotes, used for the current price calculation.</param>
		/// <param name="side">The order direction. If it is a buy, <see cref="MarketDepthPair.Ask"/> value is used, otherwise <see cref="MarketDepthPair.Bid"/>.</param>
		/// <param name="priceType">The type of current price.</param>
		/// <returns>The current price. If information in order book is insufficient, then <see langword="null" /> will be returned.</returns>
		/// <remarks>
		/// For correct operation of the method the order book export shall be launched.
		/// </remarks>
		public static Unit GetCurrentPrice(this MarketDepthPair bestPair, Sides side, MarketPriceTypes priceType = MarketPriceTypes.Following)
		{
			if (bestPair == null)
				throw new ArgumentNullException(nameof(bestPair));

			decimal? currentPrice;

			switch (priceType)
			{
				case MarketPriceTypes.Opposite:
				{
					var quote = side == Sides.Buy ? bestPair.Ask : bestPair.Bid;
					currentPrice = quote?.Price;
					break;
				}
				case MarketPriceTypes.Following:
				{
					var quote = side == Sides.Buy ? bestPair.Bid : bestPair.Ask;
					currentPrice = quote?.Price;
					break;
				}
				case MarketPriceTypes.Middle:
				{
					if (bestPair.IsFull)
						currentPrice = bestPair.Bid.Value.Price + bestPair.SpreadPrice / 2;
					else
						currentPrice = null;
					break;
				}
				default:
					throw new ArgumentOutOfRangeException(nameof(priceType), priceType, LocalizedStrings.Str1219);
			}

			return currentPrice == null
				? null
				: new Unit(currentPrice.Value).SetSecurity(bestPair.Security);
		}

		/// <summary>
		/// To use shifting for price, depending on direction <paramref name="side" />.
		/// </summary>
		/// <param name="price">Price.</param>
		/// <param name="side">The order direction, used as shift direction (for buy the shift is added, for sell - subtracted).</param>
		/// <param name="offset">Price shift.</param>
		/// <param name="security">Security.</param>
		/// <returns>New price.</returns>
		public static decimal ApplyOffset(this Unit price, Sides side, Unit offset, Security security)
		{
			if (price == null)
				throw new ArgumentNullException(nameof(price));

			if (security == null)
				throw new ArgumentNullException(nameof(security));

			if (price.GetTypeValue == null)
				price.SetSecurity(security);

			if (offset.GetTypeValue == null)
				offset.SetSecurity(security);

			return security.ShrinkPrice((decimal)(side == Sides.Buy ? price + offset : price - offset));
		}

		/// <summary>
		/// To cut the price for the order, to make it multiple of the minimal step, also to limit number of decimal places.
		/// </summary>
		/// <param name="order">The order for which the price will be cut <see cref="Order.Price"/>.</param>
		/// <param name="rule">The price rounding rule.</param>
		public static void ShrinkPrice(this Order order, ShrinkRules rule = ShrinkRules.Auto)
		{
			if (order == null)
				throw new ArgumentNullException(nameof(order));

			order.Price = order.Security.ShrinkPrice(order.Price, rule);
		}

		/// <summary>
		/// To cut the price, to make it multiple of minimal step, also to limit number of signs after the comma.
		/// </summary>
		/// <param name="security">The instrument from which the <see cref="Security.PriceStep"/> and <see cref="Security.Decimals"/> values are taken.</param>
		/// <param name="price">The price to be made multiple.</param>
		/// <param name="rule">The price rounding rule.</param>
		/// <returns>The multiple price.</returns>
		public static decimal ShrinkPrice(this Security security, decimal price, ShrinkRules rule = ShrinkRules.Auto)
		{
			//var priceStep = security.CheckPriceStep();

			return price.Round(security.PriceStep ?? 0.01m, security.Decimals ?? 0,
				rule == ShrinkRules.Auto
					? (MidpointRounding?)null
					: (rule == ShrinkRules.Less ? MidpointRounding.AwayFromZero : MidpointRounding.ToEven)).RemoveTrailingZeros();
		}

		/// <summary>
		/// To get the position on own trade.
		/// </summary>
		/// <param name="trade">Own trade, used for position calculation. At buy the trade volume <see cref="Trade.Volume"/> is taken with positive sign, at sell - with negative.</param>
		/// <returns>Position.</returns>
		public static decimal? GetPosition(this MyTrade trade)
		{
			if (trade == null)
				throw new ArgumentNullException(nameof(trade));

			var position = trade.Trade.Volume;

			if (trade.Order.Direction == Sides.Sell)
				position *= -1;

			return position;
		}

		/// <summary>
		/// To calculate profit-loss based on the portfolio.
		/// </summary>
		/// <param name="portfolio">The portfolio, for which the profit-loss shall be calculated.</param>
		/// <returns>Profit-loss.</returns>
		public static decimal? GetPnL(this Portfolio portfolio)
		{
			if (portfolio == null)
				throw new ArgumentNullException(nameof(portfolio));

			return portfolio.CurrentValue - portfolio.BeginValue;
		}

		/// <summary>
		/// To check, whether the time is traded (has the session started, ended, is there a clearing).
		/// </summary>
		/// <param name="board">Board info.</param>
		/// <param name="time">The passed time to be checked.</param>
		/// <returns><see langword="true" />, if time is traded, otherwise, not traded.</returns>
		public static bool IsTradeTime(this ExchangeBoard board, DateTimeOffset time)
		{
			return board.ToMessage().IsTradeTime(time, out _);
		}

		/// <summary>
		/// To check, whether the time is traded (has the session started, ended, is there a clearing).
		/// </summary>
		/// <param name="board">Board info.</param>
		/// <param name="time">The passed time to be checked.</param>
		/// <param name="period">Current working time period.</param>
		/// <returns><see langword="true" />, if time is traded, otherwise, not traded.</returns>
		public static bool IsTradeTime(this ExchangeBoard board, DateTimeOffset time, out WorkingTimePeriod period)
		{
			return board.ToMessage().IsTradeTime(time, out period);
		}

		/// <summary>
		/// To check, whether the time is traded (has the session started, ended, is there a clearing).
		/// </summary>
		/// <param name="board">Board info.</param>
		/// <param name="time">The passed time to be checked.</param>
		/// <returns><see langword="true" />, if time is traded, otherwise, not traded.</returns>
		public static bool IsTradeTime(this BoardMessage board, DateTimeOffset time)
		{
			return board.IsTradeTime(time, out _);
		}

		/// <summary>
		/// To check, whether the time is traded (has the session started, ended, is there a clearing).
		/// </summary>
		/// <param name="board">Board info.</param>
		/// <param name="time">The passed time to be checked.</param>
		/// <param name="period">Current working time period.</param>
		/// <returns><see langword="true" />, if time is traded, otherwise, not traded.</returns>
		public static bool IsTradeTime(this BoardMessage board, DateTimeOffset time, out WorkingTimePeriod period)
		{
			if (board == null)
				throw new ArgumentNullException(nameof(board));

			var exchangeTime = time.ToLocalTime(board.TimeZone);
			var workingTime = board.WorkingTime;

			return workingTime.IsTradeTime(exchangeTime, out period);
		}

		/// <summary>
		/// To check, whether the time is traded (has the session started, ended, is there a clearing).
		/// </summary>
		/// <param name="workingTime">Board working hours.</param>
		/// <param name="time">The passed time to be checked.</param>
		/// <param name="period">Current working time period.</param>
		/// <returns><see langword="true" />, if time is traded, otherwise, not traded.</returns>
		public static bool IsTradeTime(this WorkingTime workingTime, DateTime time, out WorkingTimePeriod period)
		{
			var isWorkingDay = workingTime.IsTradeDate(time);

			if (!isWorkingDay)
			{
				period = null;
				return false;
			}

			period = workingTime.GetPeriod(time);

			var tod = time.TimeOfDay;
			return period == null || period.Times.IsEmpty() || period.Times.Any(r => r.Contains(tod));
		}

		/// <summary>
		/// To check, whether date is traded.
		/// </summary>
		/// <param name="board">Board info.</param>
		/// <param name="date">The passed date to be checked.</param>
		/// <param name="checkHolidays">Whether to check the passed date for a weekday (Saturday and Sunday are days off, returned value for them is <see langword="false" />).</param>
		/// <returns><see langword="true" />, if the date is traded, otherwise, is not traded.</returns>
		public static bool IsTradeDate(this ExchangeBoard board, DateTimeOffset date, bool checkHolidays = false)
		{
			return board.ToMessage().IsTradeDate(date, checkHolidays);
		}

		/// <summary>
		/// To check, whether date is traded.
		/// </summary>
		/// <param name="board">Board info.</param>
		/// <param name="date">The passed date to be checked.</param>
		/// <param name="checkHolidays">Whether to check the passed date for a weekday (Saturday and Sunday are days off, returned value for them is <see langword="false" />).</param>
		/// <returns><see langword="true" />, if the date is traded, otherwise, is not traded.</returns>
		public static bool IsTradeDate(this BoardMessage board, DateTimeOffset date, bool checkHolidays = false)
		{
			if (board == null)
				throw new ArgumentNullException(nameof(board));

			var exchangeTime = date.ToLocalTime(board.TimeZone);
			var workingTime = board.WorkingTime;

			return workingTime.IsTradeDate(exchangeTime, checkHolidays);
		}

		/// <summary>
		/// To check, whether date is traded.
		/// </summary>
		/// <param name="workingTime">Board working hours.</param>
		/// <param name="date">The passed date to be checked.</param>
		/// <param name="checkHolidays">Whether to check the passed date for a weekday (Saturday and Sunday are days off, returned value for them is <see langword="false" />).</param>
		/// <returns><see langword="true" />, if the date is traded, otherwise, is not traded.</returns>
		public static bool IsTradeDate(this WorkingTime workingTime, DateTime date, bool checkHolidays = false)
		{
			var period = workingTime.GetPeriod(date);

			if ((period == null || period.Times.Count == 0) && workingTime.SpecialWorkingDays.Length == 0 && workingTime.SpecialHolidays.Length == 0)
				return true;

			bool isWorkingDay;

			if (checkHolidays && (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday))
				isWorkingDay = workingTime.SpecialWorkingDays.Contains(date.Date);
			else
				isWorkingDay = !workingTime.SpecialHolidays.Contains(date.Date);

			return isWorkingDay;
		}

		/// <summary>
		/// Get last trade date.
		/// </summary>
		/// <param name="board">Board info.</param>
		/// <param name="date">The date from which to start checking.</param>
		/// <param name="checkHolidays">Whether to check the passed date for a weekday (Saturday and Sunday are days off, returned value for them is <see langword="false" />).</param>
		/// <returns>Last trade date.</returns>
		public static DateTimeOffset LastTradeDay(this BoardMessage board, DateTimeOffset date, bool checkHolidays = true)
		{
			if (board == null)
				throw new ArgumentNullException(nameof(board));

			while (!board.IsTradeDate(date, checkHolidays))
				date = date.AddDays(-1);

			return date;
		}

		/// <summary>
		/// To create copy of the order for re-registration.
		/// </summary>
		/// <param name="oldOrder">The original order.</param>
		/// <param name="newPrice">Price of the new order.</param>
		/// <param name="newVolume">Volume of the new order.</param>
		/// <returns>New order.</returns>
		public static Order ReRegisterClone(this Order oldOrder, decimal? newPrice = null, decimal? newVolume = null)
		{
			if (oldOrder == null)
				throw new ArgumentNullException(nameof(oldOrder));

			return new Order
			{
				Portfolio = oldOrder.Portfolio,
				Direction = oldOrder.Direction,
				TimeInForce = oldOrder.TimeInForce,
				Security = oldOrder.Security,
				Type = oldOrder.Type,
				Price = newPrice ?? oldOrder.Price,
				Volume = newVolume ?? oldOrder.Volume,
				ExpiryDate = oldOrder.ExpiryDate,
				VisibleVolume = oldOrder.VisibleVolume,
				BrokerCode = oldOrder.BrokerCode,
				ClientCode = oldOrder.ClientCode,
				Condition = oldOrder.Condition?.TypedClone(),
				IsManual = oldOrder.IsManual,
				IsMarketMaker = oldOrder.IsMarketMaker,
				IsMargin = oldOrder.IsMargin,
				MinVolume = oldOrder.MinVolume,
				PositionEffect = oldOrder.PositionEffect,
				PostOnly = oldOrder.PostOnly,
			};
		}

		/// <summary>
		/// To create from regular order book a sparse on, with minimal price step of <see cref="Security.PriceStep"/>.
		/// </summary>
		/// <remarks>
		/// In sparsed book shown quotes with no active orders. The volume of these quotes is 0.
		/// </remarks>
		/// <param name="depth">The regular order book.</param>
		/// <returns>The sparse order book.</returns>
		public static MarketDepth Sparse(this MarketDepth depth)
		{
			if (depth == null)
				throw new ArgumentNullException(nameof(depth));

			return depth.Sparse(depth.Security.PriceStep ?? 1m);
		}

		/// <summary>
		/// To create from regular order book a sparse one.
		/// </summary>
		/// <remarks>
		/// In sparsed book shown quotes with no active orders. The volume of these quotes is 0.
		/// </remarks>
		/// <param name="depth">The regular order book.</param>
		/// <param name="priceStep">Minimum price step.</param>
		/// <returns>The sparse order book.</returns>
		public static MarketDepth Sparse(this MarketDepth depth, decimal priceStep)
		{
			if (depth == null)
				throw new ArgumentNullException(nameof(depth));

			var bids = depth.Bids2.Sparse(Sides.Buy, priceStep);
			var asks = depth.Asks2.Sparse(Sides.Sell, priceStep);

			var pair = depth.BestPair;
			var spreadQuotes = pair?.Sparse(priceStep);

			return new MarketDepth(depth.Security).Update(
				bids.Concat(spreadQuotes?.bids ?? ArrayHelper.Empty<QuoteChange>()).OrderByDescending(q => q.Price).ToArray(),
				asks.Concat(spreadQuotes?.asks ?? ArrayHelper.Empty<QuoteChange>()).ToArray(),
				depth.LastChangeTime);
		}

		/// <summary>
		/// To create form pair of quotes a sparse collection of quotes, which will be included into the range between the pair.
		/// </summary>
		/// <remarks>
		/// In sparsed collection shown quotes with no active orders. The volume of these quotes is 0.
		/// </remarks>
		/// <param name="pair">The pair of regular quotes.</param>
		/// <param name="priceStep">Minimum price step.</param>
		/// <returns>The sparse collection of quotes.</returns>
		public static (QuoteChange[] bids, QuoteChange[] asks) Sparse(this MarketDepthPair pair, decimal priceStep)
		{
			if (pair == null)
				throw new ArgumentNullException(nameof(pair));

			if (priceStep <= 0)
				throw new ArgumentOutOfRangeException(nameof(priceStep), priceStep, LocalizedStrings.Str1213);

			if (pair.SpreadPrice == null)
				return (ArrayHelper.Empty<QuoteChange>(), ArrayHelper.Empty<QuoteChange>());

			var bids = new List<QuoteChange>();
			var asks = new List<QuoteChange>();

			var bidPrice = pair.Bid.Value.Price;
			var askPrice = pair.Ask.Value.Price;

			while (true)
			{
				bidPrice += priceStep;
				askPrice -= priceStep;

				if (bidPrice > askPrice)
					break;

				bids.Add(new QuoteChange
				{
					//Security = security,
					Price = bidPrice,
					//OrderDirection = Sides.Buy,
				});

				if (bidPrice == askPrice)
					break;

				asks.Add(new QuoteChange
				{
					//Security = security,
					Price = askPrice,
					//OrderDirection = Sides.Sell,
				});
			}

			return (bids.ToArray(), asks.ToArray());
		}

		/// <summary>
		/// To create the sparse collection of quotes from regular quotes.
		/// </summary>
		/// <remarks>
		/// In sparsed collection shown quotes with no active orders. The volume of these quotes is 0.
		/// </remarks>
		/// <param name="quotes">Regular quotes. The collection shall contain quotes of the same direction (only bids or only offers).</param>
		/// <param name="side">Side.</param>
		/// <param name="priceStep">Minimum price step.</param>
		/// <returns>The sparse collection of quotes.</returns>
		public static IEnumerable<QuoteChange> Sparse(this IEnumerable<QuoteChange> quotes, Sides side, decimal priceStep)
		{
			if (quotes == null)
				throw new ArgumentNullException(nameof(quotes));

			if (priceStep <= 0)
				throw new ArgumentOutOfRangeException(nameof(priceStep), priceStep, LocalizedStrings.Str1213);

			var list = quotes.OrderBy(q => q.Price).ToList();

			if (list.Count < 2)
				return ArrayHelper.Empty<QuoteChange>();

			//var firstQuote = list[0];

			var retVal = new List<QuoteChange>();

			for (var i = 0; i < (list.Count - 1); i++)
			{
				var from = list[i];

				//if (from.OrderDirection != firstQuote.OrderDirection)
				//	throw new ArgumentException(LocalizedStrings.Str1214, nameof(quotes));

				var toPrice = list[i + 1].Price;

				for (var price = (from.Price + priceStep); price < toPrice; price += priceStep)
				{
					retVal.Add(new QuoteChange
					{
						//Security = firstQuote.Security,
						Price = price,
						//OrderDirection = firstQuote.OrderDirection,
					});
				}
			}

			if (side == Sides.Buy)
				return retVal.OrderByDescending(q => q.Price);
			else
				return retVal;
		}

		/// <summary>
		/// To merge the initial order book and its sparse representation.
		/// </summary>
		/// <param name="original">The initial order book.</param>
		/// <param name="rare">The sparse order book.</param>
		/// <returns>The merged order book.</returns>
		public static MarketDepth Join(this MarketDepth original, MarketDepth rare)
		{
			if (original == null)
				throw new ArgumentNullException(nameof(original));

			if (rare == null)
				throw new ArgumentNullException(nameof(rare));

			return new MarketDepth(original.Security).Update(original.Bids2.Concat(rare.Bids2).OrderByDescending(q => q.Price).ToArray(), original.Asks2.Concat(rare.Asks2).OrderBy(q => q.Price).ToArray(), original.LastChangeTime);
		}

		/// <summary>
		/// To group the order book by the price range.
		/// </summary>
		/// <param name="depth">The order book to be grouped.</param>
		/// <param name="priceRange">The price range, for which grouping shall be performed.</param>
		/// <returns>The grouped order book.</returns>
		public static MarketDepth Group(this MarketDepth depth, Unit priceRange)
		{
			return new MarketDepth(depth.Security).Update(depth.Bids2.Group(Sides.Buy, priceRange), depth.Asks2.Group(Sides.Sell, priceRange), depth.LastChangeTime);
		}

		/// <summary>
		/// To de-group the order book, grouped using the method <see cref="Group(StockSharp.BusinessEntities.MarketDepth,StockSharp.Messages.Unit)"/>.
		/// </summary>
		/// <param name="depth">The grouped order book.</param>
		/// <returns>The de-grouped order book.</returns>
		[Obsolete]
		public static MarketDepth UnGroup(this MarketDepth depth)
		{
			return new MarketDepth(depth.Security).Update(
				depth.Bids.Cast<AggregatedQuote>().SelectMany(gq => gq.InnerQuotes),
				depth.Asks.Cast<AggregatedQuote>().SelectMany(gq => gq.InnerQuotes),
				false, depth.LastChangeTime);
		}

		/// <summary>
		/// To delete in order book levels, which shall disappear in case of trades occurrence <paramref name="trades" />.
		/// </summary>
		/// <param name="depth">The order book to be cleared.</param>
		/// <param name="trades">Trades.</param>
		public static void EmulateTrades(this MarketDepth depth, IEnumerable<ExecutionMessage> trades)
		{
			if (depth == null)
				throw new ArgumentNullException(nameof(depth));

			if (trades == null)
				throw new ArgumentNullException(nameof(trades));

			var changedVolume = new Dictionary<decimal, decimal>();

			var maxTradePrice = decimal.MinValue;
			var minTradePrice = decimal.MaxValue;

			foreach (var trade in trades)
			{
				var price = trade.GetTradePrice();

				minTradePrice = minTradePrice.Min(price);
				maxTradePrice = maxTradePrice.Max(price);

				var q = depth.GetQuote(price);

				if (q is null)
					continue;

				var quote = q.Value;

				if (!changedVolume.TryGetValue(price, out var vol))
					vol = quote.Volume;

				vol -= trade.SafeGetVolume();
				changedVolume[quote.Price] = vol;
			}

			var bids = new QuoteChange[depth.Bids2.Length];

			void B1()
			{
				var i = 0;
				var count = 0;

				for (; i < depth.Bids2.Length; i++)
				{
					var quote = depth.Bids2[i];
					var price = quote.Price;

					if (price > minTradePrice)
						continue;

					if (price == minTradePrice)
					{
						if (changedVolume.TryGetValue(price, out var vol))
						{
							if (vol <= 0)
								continue;

							//quote = quote.Clone();
							quote.Volume = vol;
						}
					}

					bids[count++] = quote;
					i++;

					break;
				}

				Array.Copy(depth.Bids2, i, bids, count, depth.Bids2.Length - i);
				Array.Resize(ref bids, count + (depth.Bids2.Length - i));
			}

			B1();

			var asks = new QuoteChange[depth.Asks2.Length];

			void A1()
			{
				var i = 0;
				var count = 0;

				for (; i < depth.Asks2.Length; i++)
				{
					var quote = depth.Asks2[i];
					var price = quote.Price;

					if (price < maxTradePrice)
						continue;

					if (price == maxTradePrice)
					{
						if (changedVolume.TryGetValue(price, out var vol))
						{
							if (vol <= 0)
								continue;

							//quote = quote.Clone();
							quote.Volume = vol;
						}
					}

					asks[count++] = quote;
					i++;

					break;
				}

				Array.Copy(depth.Asks2, i, asks, count, depth.Asks2.Length - i);
				Array.Resize(ref asks, count + (depth.Asks2.Length - i));
			}

			A1();

			depth.Update(bids, asks, depth.LastChangeTime);
		}

		/// <summary>
		/// To group quotes by the price range.
		/// </summary>
		/// <param name="quotes">Quotes to be grouped.</param>
		/// <param name="side">Side.</param>
		/// <param name="priceRange">The price range, for which grouping shall be performed.</param>
		/// <returns>Grouped quotes.</returns>
		public static QuoteChange[] Group(this QuoteChange[] quotes, Sides side, Unit priceRange)
		{
			if (quotes == null)
				throw new ArgumentNullException(nameof(quotes));

			if (priceRange == null)
				throw new ArgumentNullException(nameof(priceRange));

			//if (priceRange.Value < double.Epsilon)
			//	throw new ArgumentOutOfRangeException(nameof(priceRange), priceRange, "Размер группировки меньше допустимого.");

			//if (quotes.Count() < 2)
			//	return Enumerable.Empty<AggregatedQuote>();

			var firstQuote = quotes.FirstOr();

			if (firstQuote == null)
				return ArrayHelper.Empty<QuoteChange>();

			var retVal = quotes.GroupBy(q => priceRange.AlignPrice(firstQuote.Value.Price, q.Price)).Select(g =>
			{
				decimal volume = 0;
				int? orderCount = null;

				foreach (var q in g)
				{
					volume += q.Volume;

					var oq = q.OrdersCount;

					if (oq != null)
					{
						if (orderCount == null)
							orderCount = oq;
						else
							orderCount = oq.Value;
					}
				}

				return new QuoteChange
				{
					Price = g.Key,
					Volume = volume,
					OrdersCount = orderCount,
				};
			});
			
			retVal = side == Sides.Sell ? retVal.OrderBy(q => q.Price) : retVal.OrderByDescending(q => q.Price);

			return retVal.ToArray();
		}

		private static decimal AlignPrice(this Unit priceRange, decimal firstPrice, decimal price)
		{
			if (priceRange == null)
				throw new ArgumentNullException(nameof(priceRange));

			decimal priceLevel;

			if (priceRange.Type == UnitTypes.Percent)
				priceLevel = (decimal)(firstPrice + (((price - firstPrice) * 100) / firstPrice).Floor(priceRange.Value).Percents());
			else
				priceLevel = price.Floor((decimal)priceRange);

			return priceLevel;
		}

		/// <summary>
		/// To calculate the change between order books.
		/// </summary>
		/// <param name="from">First order book.</param>
		/// <param name="to">Second order book.</param>
		/// <returns>The order book, storing only increments.</returns>
		public static QuoteChangeMessage GetDelta(this QuoteChangeMessage from, QuoteChangeMessage to)
		{
			if (from == null)
				throw new ArgumentNullException(nameof(from));

			if (to == null)
				throw new ArgumentNullException(nameof(to));

			return new QuoteChangeMessage
			{
				LocalTime = to.LocalTime,
				SecurityId = to.SecurityId,
				Bids = GetDelta(from.Bids, to.Bids, new BackwardComparer<decimal>()),
				Asks = GetDelta(from.Asks, to.Asks, null),
				ServerTime = to.ServerTime,
				State = QuoteChangeStates.Increment,
			};
		}

		/// <summary>
		/// To calculate the change between quotes.
		/// </summary>
		/// <param name="from">First quotes.</param>
		/// <param name="to">Second quotes.</param>
		/// <param name="comparer">The direction, showing the type of quotes.</param>
		/// <returns>Changes.</returns>
		private static QuoteChange[] GetDelta(this IEnumerable<QuoteChange> from, IEnumerable<QuoteChange> to, IComparer<decimal> comparer)
		{
			if (from == null)
				throw new ArgumentNullException(nameof(from));

			if (to == null)
				throw new ArgumentNullException(nameof(to));

			var mapFrom = new SortedList<decimal, QuoteChange>(comparer);
			var mapTo = new SortedList<decimal, QuoteChange>(comparer);

			foreach (var change in from)
			{
				if (!mapFrom.TryAdd(change.Price, change))
					throw new ArgumentException(LocalizedStrings.Str415Params.Put(change.Price), nameof(from));
			}

			foreach (var change in to)
			{
				if (!mapTo.TryAdd(change.Price, change))
					throw new ArgumentException(LocalizedStrings.Str415Params.Put(change.Price), nameof(to));
			}

			foreach (var pair in mapFrom)
			{
				var price = pair.Key;
				var quoteFrom = pair.Value;

				if (mapTo.TryGetValue(price, out var quoteTo))
				{
					if (quoteTo.Volume == quoteFrom.Volume &&
						quoteTo.OrdersCount == quoteFrom.OrdersCount &&
						quoteTo.Action == quoteFrom.Action &&
						quoteTo.Condition == quoteFrom.Condition &&
						quoteTo.StartPosition == quoteFrom.StartPosition &&
						quoteTo.EndPosition == quoteFrom.EndPosition)
					{
						// nothing was changes, remove this
						mapTo.Remove(price);
					}
				}
				else
				{
					// zero volume means remove price level
					mapTo[price] = new QuoteChange { Price = price };
				}
			}

			return mapTo.Values.ToArray();
		}

		/// <summary>
		/// To add change to the first order book.
		/// </summary>
		/// <param name="from">First order book.</param>
		/// <param name="delta">Change.</param>
		/// <returns>The changed order book.</returns>
		public static QuoteChangeMessage AddDelta(this QuoteChangeMessage from, QuoteChangeMessage delta)
		{
			if (from == null)
				throw new ArgumentNullException(nameof(from));

			if (delta == null)
				throw new ArgumentNullException(nameof(delta));

			if (!from.IsSorted)
				throw new ArgumentException(nameof(from));

			if (!delta.IsSorted)
				throw new ArgumentException(nameof(delta));

			return new QuoteChangeMessage
			{
				LocalTime = delta.LocalTime,
				SecurityId = from.SecurityId,
				Bids = AddDelta(from.Bids, delta.Bids, true),
				Asks = AddDelta(from.Asks, delta.Asks, false),
				ServerTime = delta.ServerTime,
			};
		}

		/// <summary>
		/// To add change to quote.
		/// </summary>
		/// <param name="fromQuotes">Quotes.</param>
		/// <param name="deltaQuotes">Changes.</param>
		/// <param name="isBids">The indication of quotes direction.</param>
		/// <returns>Changed quotes.</returns>
		public static QuoteChange[] AddDelta(this IEnumerable<QuoteChange> fromQuotes, IEnumerable<QuoteChange> deltaQuotes, bool isBids)
		{
			var result = new List<QuoteChange>();

			using (var fromEnu = fromQuotes.GetEnumerator())
			{
				var hasFrom = fromEnu.MoveNext();

				foreach (var quoteChange in deltaQuotes)
				{
					var canAdd = true;

					while (hasFrom)
					{
						var current = fromEnu.Current;

						if (isBids)
						{
							if (current.Price > quoteChange.Price)
								result.Add(current);
							else if (current.Price == quoteChange.Price)
							{
								if (quoteChange.Volume != 0)
									result.Add(quoteChange);

								hasFrom = fromEnu.MoveNext();
								canAdd = false;

								break;
							}
							else
								break;
						}
						else
						{
							if (current.Price < quoteChange.Price)
								result.Add(current);
							else if (current.Price == quoteChange.Price)
							{
								if (quoteChange.Volume != 0)
									result.Add(quoteChange);

								hasFrom = fromEnu.MoveNext();
								canAdd = false;

								break;
							}
							else
								break;
						}

						hasFrom = fromEnu.MoveNext();
					}

					if (canAdd && quoteChange.Volume != 0)
						result.Add(quoteChange);
				}

				while (hasFrom)
				{
					result.Add(fromEnu.Current);
					hasFrom = fromEnu.MoveNext();
				}
			}

			return result.ToArray();
		}

		/// <summary>
		/// To check, whether the order was cancelled.
		/// </summary>
		/// <param name="order">The order to be checked.</param>
		/// <returns><see langword="true" />, if the order is cancelled, otherwise, <see langword="false" />.</returns>
		public static bool IsCanceled(this Order order)
		{
			return order.ToMessage().IsCanceled();
		}

		/// <summary>
		/// To check, is the order matched completely.
		/// </summary>
		/// <param name="order">The order to be checked.</param>
		/// <returns><see langword="true" />, if the order is matched completely, otherwise, <see langword="false" />.</returns>
		public static bool IsMatched(this Order order)
		{
			return order.ToMessage().IsMatched();
		}

		/// <summary>
		/// To check, is a part of volume is implemented in the order.
		/// </summary>
		/// <param name="order">The order to be checked.</param>
		/// <returns><see langword="true" />, if part of volume is implemented, otherwise, <see langword="false" />.</returns>
		public static bool IsMatchedPartially(this Order order)
		{
			return order.ToMessage().IsMatchedPartially();
		}

		/// <summary>
		/// To check, if no contract in order is implemented.
		/// </summary>
		/// <param name="order">The order to be checked.</param>
		/// <returns><see langword="true" />, if no contract is implemented, otherwise, <see langword="false" />.</returns>
		public static bool IsMatchedEmpty(this Order order)
		{
			return order.ToMessage().IsMatchedEmpty();
		}

		/// <summary>
		/// To check, whether the order was cancelled.
		/// </summary>
		/// <param name="order">The order to be checked.</param>
		/// <returns><see langword="true" />, if the order is cancelled, otherwise, <see langword="false" />.</returns>
		public static bool IsCanceled(this ExecutionMessage order)
		{
			if (order == null)
				throw new ArgumentNullException(nameof(order));

			return order.OrderState == OrderStates.Done && order.Balance > 0;
		}

		/// <summary>
		/// To check, is the order matched completely.
		/// </summary>
		/// <param name="order">The order to be checked.</param>
		/// <returns><see langword="true" />, if the order is matched completely, otherwise, <see langword="false" />.</returns>
		public static bool IsMatched(this ExecutionMessage order)
		{
			if (order == null)
				throw new ArgumentNullException(nameof(order));

			return order.OrderState == OrderStates.Done && order.Balance == 0;
		}

		/// <summary>
		/// To check, is a part of volume is implemented in the order.
		/// </summary>
		/// <param name="order">The order to be checked.</param>
		/// <returns><see langword="true" />, if part of volume is implemented, otherwise, <see langword="false" />.</returns>
		public static bool IsMatchedPartially(this ExecutionMessage order)
		{
			if (order == null)
				throw new ArgumentNullException(nameof(order));

			return order.Balance > 0 && order.Balance != order.OrderVolume;
		}

		/// <summary>
		/// To check, if no contract in order is implemented.
		/// </summary>
		/// <param name="order">The order to be checked.</param>
		/// <returns><see langword="true" />, if no contract is implemented, otherwise, <see langword="false" />.</returns>
		public static bool IsMatchedEmpty(this ExecutionMessage order)
		{
			if (order == null)
				throw new ArgumentNullException(nameof(order));

			return order.Balance > 0 && order.Balance == order.OrderVolume;
		}

		/// <summary>
		/// To calculate the implemented part of volume for order.
		/// </summary>
		/// <param name="order">The order, for which the implemented part of volume shall be calculated.</param>
		/// <returns>The implemented part of volume.</returns>
		public static decimal GetMatchedVolume(this Order order)
		{
			if (order == null)
				throw new ArgumentNullException(nameof(order));

			if (order.Type == OrderTypes.Conditional)
				throw new ArgumentException(nameof(order));

			return order.Volume - order.Balance;
		}

		/// <summary>
		/// To get the weighted mean price of matching by own trades.
		/// </summary>
		/// <param name="trades">Trades, for which the weighted mean price of matching shall be got.</param>
		/// <returns>The weighted mean price. If no trades, 0 is returned.</returns>
		public static decimal GetAveragePrice(this IEnumerable<MyTrade> trades)
		{
			if (trades == null)
				throw new ArgumentNullException(nameof(trades));

			var numerator = 0m;
			var denominator = 0m;
			var currentAvgPrice = 0m;

			foreach (var myTrade in trades)
			{
				var order = myTrade.Order;
				var trade = myTrade.Trade;

				var direction = (order.Direction == Sides.Buy) ? 1m : -1m;

				//Если открываемся или переворачиваемся
				if (direction != denominator.Sign() && trade.Volume > denominator.Abs())
				{
					var newVolume = trade.Volume - denominator.Abs();
					numerator = direction * trade.Price * newVolume;
					denominator = direction * newVolume;
				}
				else
				{
					//Если добавляемся в сторону уже открытой позиции
					if (direction == denominator.Sign())
						numerator += direction * trade.Price * trade.Volume;
					else
						numerator += direction * currentAvgPrice * trade.Volume;

					denominator += direction * trade.Volume;
				}

				currentAvgPrice = (denominator != 0) ? numerator / denominator : 0m;
			}

			return currentAvgPrice;
		}

		/// <summary>
		/// To get probable trades for order book for the given order.
		/// </summary>
		/// <param name="depth">The order book, reflecting situation on market at the moment of function call.</param>
		/// <param name="order">The order, for which probable trades shall be calculated.</param>
		/// <returns>Probable trades.</returns>
		public static IEnumerable<MyTrade> GetTheoreticalTrades(this MarketDepth depth, Order order)
		{
			if (depth == null)
				throw new ArgumentNullException(nameof(depth));

			if (order == null)
				throw new ArgumentNullException(nameof(order));

			if (depth.Security != order.Security)
				throw new ArgumentException(nameof(order));

			order = order.ReRegisterClone();
			depth = depth.Clone();

			order.LastChangeTime = depth.LastChangeTime = DateTimeOffset.Now;
			order.LocalTime = depth.LocalTime = DateTime.Now;

			var testPf = Portfolio.CreateSimulator();
			order.Portfolio = testPf;

			var trades = new List<MyTrade>();

			using (IMarketEmulator emulator = new MarketEmulator(new CollectionSecurityProvider(new[] { order.Security }), new CollectionPortfolioProvider(new[] { testPf }), new InMemoryExchangeInfoProvider()))
			{
				var errors = new List<Exception>();

				emulator.NewOutMessage += msg =>
				{
					if (!(msg is ExecutionMessage execMsg))
						return;

					if (execMsg.Error != null)
						errors.Add(execMsg.Error);

					if (execMsg.HasTradeInfo())
					{
						trades.Add(new MyTrade
						{
							Order = order,
							Trade = execMsg.ToTrade(new Trade { Security = order.Security })
						});
					}
				};

				var depthMsg = depth.ToMessage();
				var regMsg = order.CreateRegisterMessage();
				var pfMsg = testPf.ToChangeMessage();

				pfMsg.ServerTime = depthMsg.ServerTime = order.LastChangeTime;
				pfMsg.LocalTime = regMsg.LocalTime = depthMsg.LocalTime = order.LocalTime;

				emulator.SendInMessage(pfMsg);
				emulator.SendInMessage(depthMsg);
				emulator.SendInMessage(regMsg);

				if (errors.Count > 0)
					throw new AggregateException(errors);
			}

			return trades;
		}

		/// <summary>
		/// To get probable trades by the order book for the market price and given volume.
		/// </summary>
		/// <param name="depth">The order book, reflecting situation on market at the moment of function call.</param>
		/// <param name="orderDirection">Order side.</param>
		/// <param name="volume">The volume, supposed to be implemented.</param>
		/// <returns>Probable trades.</returns>
		public static IEnumerable<MyTrade> GetTheoreticalTrades(this MarketDepth depth, Sides orderDirection, decimal volume)
		{
			return depth.GetTheoreticalTrades(orderDirection, volume, 0);
		}

		/// <summary>
		/// To get probable trades by order book for given price and volume.
		/// </summary>
		/// <param name="depth">The order book, reflecting situation on market at the moment of function call.</param>
		/// <param name="orderDirection">Order side.</param>
		/// <param name="volume">The volume, supposed to be implemented.</param>
		/// <param name="price">The price, based on which the order is supposed to be forwarded. If it equals 0, option of market order will be considered.</param>
		/// <returns>Probable trades.</returns>
		public static IEnumerable<MyTrade> GetTheoreticalTrades(this MarketDepth depth, Sides orderDirection, decimal volume, decimal price)
		{
			if (depth == null)
				throw new ArgumentNullException(nameof(depth));

			return depth.GetTheoreticalTrades(new Order
			{
				Direction = orderDirection,
				Type = price == 0 ? OrderTypes.Market : OrderTypes.Limit,
				Security = depth.Security,
				Price = price,
				Volume = volume
			});
		}

		/// <summary>
		/// To change the direction to opposite.
		/// </summary>
		/// <param name="side">The initial direction.</param>
		/// <returns>The opposite direction.</returns>
		public static Sides Invert(this Sides side)
		{
			return side == Sides.Buy ? Sides.Sell : Sides.Buy;
		}

		/// <summary>
		/// To get the order direction for the position.
		/// </summary>
		/// <param name="position">The position value.</param>
		/// <returns>Order side.</returns>
		/// <remarks>
		/// A positive value equals <see cref="Sides.Buy"/>, a negative - <see cref="Sides.Sell"/>, zero - <see langword="null" />.
		/// </remarks>
		public static Sides? GetDirection(this Position position)
		{
			if (position == null)
				throw new ArgumentNullException(nameof(position));

			return position.CurrentValue?.GetDirection();
		}

		/// <summary>
		/// To get the order direction for the position.
		/// </summary>
		/// <param name="position">The position value.</param>
		/// <returns>Order side.</returns>
		/// <remarks>
		/// A positive value equals <see cref="Sides.Buy"/>, a negative - <see cref="Sides.Sell"/>, zero - <see langword="null" />.
		/// </remarks>
		public static Sides? GetDirection(this decimal position)
		{
			if (position == 0)
				return null;

			return position > 0 ? Sides.Buy : Sides.Sell;
		}

		/// <summary>
		/// Cancel orders by filter.
		/// </summary>
		/// <param name="connector">The connection of interaction with trade systems.</param>
		/// <param name="orders">The group of orders, from which the required orders shall be found and cancelled.</param>
		/// <param name="isStopOrder"><see langword="true" />, if cancel only a stop orders, <see langword="false" /> - if regular orders, <see langword="null" /> - both.</param>
		/// <param name="portfolio">Portfolio. If the value is equal to <see langword="null" />, then the portfolio does not match the orders cancel filter.</param>
		/// <param name="direction">Order side. If the value is <see langword="null" />, the direction does not use.</param>
		/// <param name="board">Trading board. If the value is equal to <see langword="null" />, then the board does not match the orders cancel filter.</param>
		/// <param name="security">Instrument. If the value is equal to <see langword="null" />, then the instrument does not match the orders cancel filter.</param>
		/// <param name="securityType">Security type. If the value is <see langword="null" />, the type does not use.</param>
		public static void CancelOrders(this IConnector connector, IEnumerable<Order> orders, bool? isStopOrder = null, Portfolio portfolio = null, Sides? direction = null, ExchangeBoard board = null, Security security = null, SecurityTypes? securityType = null)
		{
			if (connector == null)
				throw new ArgumentNullException(nameof(connector));

			if (orders == null)
				throw new ArgumentNullException(nameof(orders));

			orders = orders
				.Where(order => !order.State.IsFinal())
				.Where(order => isStopOrder == null || (order.Type == OrderTypes.Conditional) == isStopOrder.Value)
				.Where(order => portfolio == null || (order.Portfolio == portfolio))
				.Where(order => direction == null || order.Direction == direction.Value)
				.Where(order => board == null || order.Security.Board == board)
				.Where(order => security == null || order.Security == security)
				.Where(order => securityType == null || order.Security.Type == securityType.Value)
				;

			orders.ForEach(connector.CancelOrder);
		}

		/// <summary>
		/// Is the specified state is final (<see cref="OrderStates.Done"/> or <see cref="OrderStates.Failed"/>).
		/// </summary>
		/// <param name="state">Order state.</param>
		/// <returns>Check result.</returns>
		public static bool IsFinal(this OrderStates state)
			=> state == OrderStates.Done || state == OrderStates.Failed;

		/// <summary>
		/// To check whether specified instrument is used now.
		/// </summary>
		/// <param name="basketSecurity">Instruments basket.</param>
		/// <param name="securityProvider">The provider of information about instruments.</param>
		/// <param name="security">The instrument that should be checked.</param>
		/// <returns><see langword="true" />, if specified instrument is used now, otherwise <see langword="false" />.</returns>
		public static bool Contains(this BasketSecurity basketSecurity, ISecurityProvider securityProvider, Security security)
		{
			return basketSecurity.GetInnerSecurities(securityProvider).Any(innerSecurity =>
			{
				if (innerSecurity is BasketSecurity basket)
					return basket.Contains(securityProvider, security);
				
				return innerSecurity == security;
			});
		}

		/// <summary>
		/// Find inner security instances.
		/// </summary>
		/// <param name="security">Instruments basket.</param>
		/// <param name="securityProvider">The provider of information about instruments.</param>
		/// <returns>Instruments, from which this basket is created.</returns>
		public static IEnumerable<Security> GetInnerSecurities(this BasketSecurity security, ISecurityProvider securityProvider)
		{
			if (security == null)
				throw new ArgumentNullException(nameof(security));

			if (securityProvider == null)
				throw new ArgumentNullException(nameof(securityProvider));

			return security.InnerSecurityIds.Select(id =>
			{
				var innerSec = securityProvider.LookupById(id);

				if (innerSec == null)
					throw new InvalidOperationException(LocalizedStrings.Str704Params.Put(id));

				return innerSec;
			}).ToArray();
		}

		/// <summary>
		/// To filter orders for the given instrument.
		/// </summary>
		/// <param name="orders">All orders, in which the required shall be searched for.</param>
		/// <param name="security">The instrument, for which the orders shall be filtered.</param>
		/// <returns>Filtered orders.</returns>
		public static IEnumerable<Order> Filter(this IEnumerable<Order> orders, Security security)
		{
			if (orders == null)
				throw new ArgumentNullException(nameof(orders));

			if (security == null)
				throw new ArgumentNullException(nameof(security));

			var basket = security as BasketSecurity;
			return basket?.InnerSecurityIds.SelectMany(id => orders.Where(o => o.Security.ToSecurityId() == id)) ?? orders.Where(o => o.Security == security);
		}

		/// <summary>
		/// To filter orders for the given portfolio.
		/// </summary>
		/// <param name="orders">All orders, in which the required shall be searched for.</param>
		/// <param name="portfolio">The portfolio, for which the orders shall be filtered.</param>
		/// <returns>Filtered orders.</returns>
		public static IEnumerable<Order> Filter(this IEnumerable<Order> orders, Portfolio portfolio)
		{
			if (orders == null)
				throw new ArgumentNullException(nameof(orders));

			if (portfolio == null)
				throw new ArgumentNullException(nameof(portfolio));

			return orders.Where(p => p.Portfolio == portfolio);
		}

		/// <summary>
		/// To filter orders for the given condition.
		/// </summary>
		/// <param name="orders">All orders, in which the required shall be searched for.</param>
		/// <param name="state">Order state.</param>
		/// <returns>Filtered orders.</returns>
		public static IEnumerable<Order> Filter(this IEnumerable<Order> orders, OrderStates state)
		{
			if (orders == null)
				throw new ArgumentNullException(nameof(orders));

			return orders.Where(p => p.State == state);
		}

		/// <summary>
		/// To filter orders for the given direction.
		/// </summary>
		/// <param name="orders">All orders, in which the required shall be searched for.</param>
		/// <param name="direction">Order side.</param>
		/// <returns>Filtered orders.</returns>
		public static IEnumerable<Order> Filter(this IEnumerable<Order> orders, Sides direction)
		{
			if (orders == null)
				throw new ArgumentNullException(nameof(orders));

			return orders.Where(p => p.Direction == direction);
		}

		/// <summary>
		/// To filter orders for the given instrument.
		/// </summary>
		/// <param name="trades">All trades, in which the required shall be searched for.</param>
		/// <param name="security">The instrument, for which the trades shall be filtered.</param>
		/// <returns>Filtered trades.</returns>
		public static IEnumerable<Trade> Filter(this IEnumerable<Trade> trades, Security security)
		{
			if (trades == null)
				throw new ArgumentNullException(nameof(trades));

			if (security == null)
				throw new ArgumentNullException(nameof(security));

			var basket = security as BasketSecurity;
			return basket?.InnerSecurityIds.SelectMany(id => trades.Where(o => o.Security.ToSecurityId() == id)) ?? trades.Where(t => t.Security == security);
		}

		/// <summary>
		/// To filter trades for the given time period.
		/// </summary>
		/// <param name="trades">All trades, in which the required shall be searched for.</param>
		/// <param name="from">The start date for trades searching.</param>
		/// <param name="to">The end date for trades searching.</param>
		/// <returns>Filtered trades.</returns>
		public static IEnumerable<Trade> Filter(this IEnumerable<Trade> trades, DateTimeOffset from, DateTimeOffset to)
		{
			if (trades == null)
				throw new ArgumentNullException(nameof(trades));

			return trades.Where(trade => trade.Time >= from && trade.Time < to);
		}

		/// <summary>
		/// To filter positions for the given instrument.
		/// </summary>
		/// <param name="positions">All positions, in which the required shall be searched for.</param>
		/// <param name="security">The instrument, for which positions shall be filtered.</param>
		/// <returns>Filtered positions.</returns>
		public static IEnumerable<Position> Filter(this IEnumerable<Position> positions, Security security)
		{
			if (positions == null)
				throw new ArgumentNullException(nameof(positions));

			if (security == null)
				throw new ArgumentNullException(nameof(security));

			var basket = security as BasketSecurity;
			return basket?.InnerSecurityIds.SelectMany(id => positions.Where(o => o.Security.ToSecurityId() == id)) ?? positions.Where(p => p.Security == security);
		}

		/// <summary>
		/// To filter positions for the given portfolio.
		/// </summary>
		/// <param name="positions">All positions, in which the required shall be searched for.</param>
		/// <param name="portfolio">The portfolio, for which positions shall be filtered.</param>
		/// <returns>Filtered positions.</returns>
		public static IEnumerable<Position> Filter(this IEnumerable<Position> positions, Portfolio portfolio)
		{
			if (positions == null)
				throw new ArgumentNullException(nameof(positions));

			if (portfolio == null)
				throw new ArgumentNullException(nameof(portfolio));

			return positions.Where(p => p.Portfolio == portfolio);
		}

		/// <summary>
		/// To filter own trades for the given instrument.
		/// </summary>
		/// <param name="myTrades">All own trades, in which the required shall be looked for.</param>
		/// <param name="security">The instrument, on which the trades shall be found.</param>
		/// <returns>Filtered trades.</returns>
		public static IEnumerable<MyTrade> Filter(this IEnumerable<MyTrade> myTrades, Security security)
		{
			if (myTrades == null)
				throw new ArgumentNullException(nameof(myTrades));

			if (security == null)
				throw new ArgumentNullException(nameof(security));

			var basket = security as BasketSecurity;
			return basket?.InnerSecurityIds.SelectMany(id => myTrades.Where(t => t.Order.Security.ToSecurityId() == id)) ?? myTrades.Where(t => t.Order.Security == security);
		}

		/// <summary>
		/// To filter own trades for the given portfolio.
		/// </summary>
		/// <param name="myTrades">All own trades, in which the required shall be looked for.</param>
		/// <param name="portfolio">The portfolio, for which the trades shall be filtered.</param>
		/// <returns>Filtered trades.</returns>
		public static IEnumerable<MyTrade> Filter(this IEnumerable<MyTrade> myTrades, Portfolio portfolio)
		{
			if (myTrades == null)
				throw new ArgumentNullException(nameof(myTrades));

			if (portfolio == null)
				throw new ArgumentNullException(nameof(portfolio));

			return myTrades.Where(t => t.Order.Portfolio == portfolio);
		}

		/// <summary>
		/// To filter own trades for the given order.
		/// </summary>
		/// <param name="myTrades">All own trades, in which the required shall be looked for.</param>
		/// <param name="order">The order, for which trades shall be filtered.</param>
		/// <returns>Filtered orders.</returns>
		public static IEnumerable<MyTrade> Filter(this IEnumerable<MyTrade> myTrades, Order order)
		{
			if (myTrades == null)
				throw new ArgumentNullException(nameof(myTrades));

			if (order == null)
				throw new ArgumentNullException(nameof(order));

			return myTrades.Where(t => t.Order == order);
		}

		/// <summary>
		/// To create the search criteria <see cref="Security"/> from <see cref="SecurityLookupMessage"/>.
		/// </summary>
		/// <param name="connector">Connection to the trading system.</param>
		/// <param name="criteria">The criterion which fields will be used as a filter.</param>
		/// <param name="exchangeInfoProvider">Exchanges and trading boards provider.</param>
		/// <returns>Search criterion.</returns>
		public static Security GetSecurityCriteria(this Connector connector, SecurityLookupMessage criteria, IExchangeInfoProvider exchangeInfoProvider)
		{
			if (connector == null)
				throw new ArgumentNullException(nameof(connector));

			if (criteria == null)
				throw new ArgumentNullException(nameof(criteria));

			if (exchangeInfoProvider == null)
				throw new ArgumentNullException(nameof(exchangeInfoProvider));

			var stocksharpId = criteria.SecurityId.SecurityCode.IsEmpty() || criteria.SecurityId.BoardCode.IsEmpty()
				                   ? string.Empty
				                   : connector.SecurityIdGenerator.GenerateId(criteria.SecurityId.SecurityCode, criteria.SecurityId.BoardCode);

			var secCriteria = new Security { Id = stocksharpId };
			secCriteria.ApplyChanges(criteria, exchangeInfoProvider);
			return secCriteria;
		}

		/// <summary>
		/// To filter instruments by the trading board.
		/// </summary>
		/// <param name="securities">Securities.</param>
		/// <param name="board">Trading board.</param>
		/// <returns>Instruments filtered.</returns>
		public static IEnumerable<Security> Filter(this IEnumerable<Security> securities, ExchangeBoard board)
		{
			if (securities == null)
				throw new ArgumentNullException(nameof(securities));

			if (board == null)
				throw new ArgumentNullException(nameof(board));

			return securities.Where(s => s.Board == board);
		}

		/// <summary>
		/// To filter instruments by the given criteria.
		/// </summary>
		/// <param name="securities">Securities.</param>
		/// <param name="criteria">The instrument whose fields will be used as a filter.</param>
		/// <returns>Instruments filtered.</returns>
		public static IEnumerable<Security> Filter(this IEnumerable<Security> securities, Security criteria)
		{
			return securities.Filter(criteria.ToLookupMessage());
		}

		/// <summary>
		/// To filter instruments by the given criteria.
		/// </summary>
		/// <param name="securities">Securities.</param>
		/// <param name="criteria">Message security lookup for specified criteria.</param>
		/// <returns>Instruments filtered.</returns>
		public static IEnumerable<Security> Filter(this IEnumerable<Security> securities, SecurityLookupMessage criteria)
		{
			if (securities == null)
				throw new ArgumentNullException(nameof(securities));

			if (criteria.IsLookupAll())
				return securities.ToArray();

			var dict = securities.ToDictionary(s => s.ToMessage(), s => s);
			return dict.Keys.Filter(criteria).Select(m => dict[m]).ToArray();
		}

		/// <summary>
		/// To determine, is the order book empty.
		/// </summary>
		/// <param name="depth">Market depth.</param>
		/// <returns><see langword="true" />, if order book is empty, otherwise, <see langword="false" />.</returns>
		public static bool IsFullEmpty(this MarketDepth depth)
		{
			if (depth == null)
				throw new ArgumentNullException(nameof(depth));

			return depth.Bids2.Length ==0 && depth.Asks2.Length == 0;
		}

		/// <summary>
		/// To determine, is the order book half-empty.
		/// </summary>
		/// <param name="depth">Market depth.</param>
		/// <returns><see langword="true" />, if the order book is half-empty, otherwise, <see langword="false" />.</returns>
		public static bool IsHalfEmpty(this MarketDepth depth)
		{
			if (depth == null)
				throw new ArgumentNullException(nameof(depth));

			var pair = depth.BestPair;

			if (pair.Bid == null)
				return pair.Ask != null;
			else
				return pair.Ask == null;
		}

		/// <summary>
		/// To get date of day T +/- of N trading days.
		/// </summary>
		/// <param name="board">Board info.</param>
		/// <param name="date">The start T date, to which are added or subtracted N trading days.</param>
		/// <param name="n">The N size. The number of trading days for the addition or subtraction.</param>
		/// <param name="checkHolidays">Whether to check the passed date for a weekday (Saturday and Sunday are days off, returned value for them is <see langword="false" />).</param>
		/// <returns>The end T +/- N date.</returns>
		public static DateTimeOffset AddOrSubtractTradingDays(this ExchangeBoard board, DateTimeOffset date, int n, bool checkHolidays = true)
		{
			if (board == null)
				throw new ArgumentNullException(nameof(board));

			while (n != 0)
			{
				//if need to Add
				if (n > 0)
				{
					date = date.AddDays(1);
					if (board.IsTradeDate(date, checkHolidays)) n--;
				}
				//if need to Subtract
				if (n < 0)
				{
					date = date.AddDays(-1);
					if (board.IsTradeDate(date, checkHolidays)) n++;
				}
			}

			return date;
		}

		/// <summary>
		/// To get the expiration date for <see cref="ExchangeBoard.Forts"/>.
		/// </summary>
		/// <param name="from">The start of the expiration range.</param>
		/// <param name="to">The end of the expiration range.</param>
		/// <returns>Expiration dates.</returns>
		public static IEnumerable<DateTimeOffset> GetExpiryDates(this DateTime from, DateTime to)
		{
			if (from > to)
				throw new ArgumentOutOfRangeException(nameof(to), to, LocalizedStrings.Str1014.Put(from));

			for (var year = from.Year; year <= to.Year; year++)
			{
				var monthFrom = year == from.Year ? from.Month : 1;
				var monthTo = year == to.Year ? to.Month : 12;

				for (var month = monthFrom; month <= monthTo; month++)
				{
					switch (month)
					{
						case 3:
						case 6:
						case 9:
						case 12:
						{
							var dt = new DateTime(year, month, 15).ApplyTimeZone(ExchangeBoard.Forts.TimeZone);

							while (!ExchangeBoard.Forts.IsTradeDate(dt))
							{
								dt = dt.AddDays(1);
							}
							yield return dt;
							break;
						}
						
						default:
							continue;
					}
				}
			}
		}

		/// <summary>
		/// To get real expiration instruments for base part of the code.
		/// </summary>
		/// <param name="baseCode">The base part of the instrument code.</param>
		/// <param name="from">The start of the expiration range.</param>
		/// <param name="to">The end of the expiration range.</param>
		/// <param name="getSecurity">The function to get instrument by the code.</param>
		/// <param name="throwIfNotExists">To generate exception, if some of instruments are not available.</param>
		/// <returns>Expiration instruments.</returns>
		public static IEnumerable<Security> GetFortsJumps(this string baseCode, DateTime from, DateTime to, Func<string, Security> getSecurity, bool throwIfNotExists = true)
		{
			if (baseCode.IsEmpty())
				throw new ArgumentNullException(nameof(baseCode));

			if (from > to)
				throw new ArgumentOutOfRangeException(nameof(to), to, LocalizedStrings.Str1014.Put(from));

			if (getSecurity == null)
				throw new ArgumentNullException(nameof(getSecurity));

			for (var year = from.Year; year <= to.Year; year++)
			{
				var monthFrom = year == from.Year ? from.Month : 1;
				var monthTo = year == to.Year ? to.Month : 12;

				for (var month = monthFrom; month <= monthTo; month++)
				{
					char monthCode;

					switch (month)
					{
						case 3:
							monthCode = 'H';
							break;
						case 6:
							monthCode = 'M';
							break;
						case 9:
							monthCode = 'U';
							break;
						case 12:
							monthCode = 'Z';
							break;
						default:
							continue;
					}

					var yearStr = year.To<string>();
					var code = baseCode + monthCode + yearStr.Substring(yearStr.Length - 1, 1);

					var security = getSecurity(code);

					if (security == null)
					{
						if (throwIfNotExists)
							throw new InvalidOperationException(LocalizedStrings.Str704Params.Put(code));

						continue;
					}
					
					yield return security;
				}
			}
		}

		/// <summary>
		/// To get real expiration instruments for the continuous instrument.
		/// </summary>
		/// <param name="continuousSecurity">Continuous security.</param>
		/// <param name="provider">The provider of information about instruments.</param>
		/// <param name="baseCode">The base part of the instrument code.</param>
		/// <param name="from">The start of the expiration range.</param>
		/// <param name="to">The end of the expiration range.</param>
		/// <param name="throwIfNotExists">To generate exception, if some of instruments for passed <paramref name="continuousSecurity" /> are not available.</param>
		/// <returns>Expiration instruments.</returns>
		public static IEnumerable<Security> GetFortsJumps(this ExpirationContinuousSecurity continuousSecurity, ISecurityProvider provider, string baseCode, DateTime from, DateTime to, bool throwIfNotExists = true)
		{
			if (continuousSecurity == null)
				throw new ArgumentNullException(nameof(continuousSecurity));

			if (provider == null)
				throw new ArgumentNullException(nameof(provider));

			return baseCode.GetFortsJumps(from, to, code => provider.LookupByCode(code).FirstOrDefault(s => s.Code.CompareIgnoreCase(code)), throwIfNotExists);
		}

		/// <summary>
		/// To fill transitions <see cref="ExpirationContinuousSecurity.ExpirationJumps"/>.
		/// </summary>
		/// <param name="continuousSecurity">Continuous security.</param>
		/// <param name="provider">The provider of information about instruments.</param>
		/// <param name="baseCode">The base part of the instrument code.</param>
		/// <param name="from">The start of the expiration range.</param>
		/// <param name="to">The end of the expiration range.</param>
		public static void FillFortsJumps(this ExpirationContinuousSecurity continuousSecurity, ISecurityProvider provider, string baseCode, DateTime from, DateTime to)
		{
			var securities = continuousSecurity.GetFortsJumps(provider, baseCode, from, to);

			foreach (var security in securities)
			{
				var expDate = security.ExpiryDate;

				if (expDate == null)
					throw new InvalidOperationException(LocalizedStrings.Str698Params.Put(security.Id));

				continuousSecurity.ExpirationJumps.Add(security.ToSecurityId(), expDate.Value);
			}
		}

		/// <summary>
		/// Write order info to the log.
		/// </summary>
		/// <param name="receiver">Logs receiver.</param>
		/// <param name="order">Order.</param>
		/// <param name="operation">Order action name.</param>
		/// <param name="getAdditionalInfo">Extended order info.</param>
		public static void AddOrderInfoLog(this ILogReceiver receiver, Order order, string operation, Func<string> getAdditionalInfo = null)
		{
			receiver.AddOrderLog(LogLevels.Info, order, operation, getAdditionalInfo);
		}

		/// <summary>
		/// Write order error to the log.
		/// </summary>
		/// <param name="receiver">Logs receiver.</param>
		/// <param name="order">Order.</param>
		/// <param name="operation">Order action name.</param>
		/// <param name="getAdditionalInfo">Extended order info.</param>
		public static void AddOrderErrorLog(this ILogReceiver receiver, Order order, string operation, Func<string> getAdditionalInfo = null)
		{
			receiver.AddOrderLog(LogLevels.Error, order, operation, getAdditionalInfo);
		}

		private static void AddOrderLog(this ILogReceiver receiver, LogLevels type, Order order, string operation, Func<string> getAdditionalInfo)
		{
			if (receiver == null)
				throw new ArgumentNullException(nameof(receiver));

			if (order == null)
				throw new ArgumentNullException(nameof(order));

			var orderDescription = order.ToString();
			var additionalInfo = getAdditionalInfo == null ? string.Empty : getAdditionalInfo();

			receiver.AddLog(new LogMessage(receiver, receiver.CurrentTime, type, () => "{0}: {1} {2}".Put(operation, orderDescription, additionalInfo)));
		}

		/// <summary>
		/// Change subscription state.
		/// </summary>
		/// <param name="currState">Current state.</param>
		/// <param name="newState">New state.</param>
		/// <param name="subscriptionId">Subscription id.</param>
		/// <param name="receiver">Logs.</param>
		/// <param name="isInfoLevel">Use <see cref="LogLevels.Info"/> for log message.</param>
		/// <returns>New state.</returns>
		public static SubscriptionStates ChangeSubscriptionState(this SubscriptionStates currState, SubscriptionStates newState, long subscriptionId, ILogReceiver receiver, bool isInfoLevel = true)
		{
			bool isOk;

			if (currState == newState)
				isOk = false;
			else
			{
				switch (currState)
				{
					case SubscriptionStates.Stopped:
					case SubscriptionStates.Active:
						isOk = true;
						break;
					case SubscriptionStates.Error:
					case SubscriptionStates.Finished:
						isOk = false;
						break;
					case SubscriptionStates.Online:
						isOk = newState != SubscriptionStates.Active;
						break;
					default:
						throw new ArgumentOutOfRangeException(nameof(currState), currState, LocalizedStrings.Str1219);
				}
			}

			const string text = "Subscription {0} {1}->{2}.";

			if (isOk)
			{
				if (isInfoLevel)
					receiver.AddInfoLog(text, subscriptionId, currState, newState);
				else
					receiver.AddDebugLog(text, subscriptionId, currState, newState);
			}
			else
				receiver.AddWarningLog(text, subscriptionId, currState, newState);

			return newState;
		}

		/// <summary>
		/// Apply changes to the portfolio object.
		/// </summary>
		/// <param name="portfolio">Portfolio.</param>
		/// <param name="message">Portfolio change message.</param>
		/// <param name="exchangeInfoProvider">Exchanges and trading boards provider.</param>
		public static void ApplyChanges(this Portfolio portfolio, PositionChangeMessage message, IExchangeInfoProvider exchangeInfoProvider)
		{
			if (portfolio == null)
				throw new ArgumentNullException(nameof(portfolio));

			if (message == null)
				throw new ArgumentNullException(nameof(message));

			if (exchangeInfoProvider == null)
				throw new ArgumentNullException(nameof(exchangeInfoProvider));

			if (!message.BoardCode.IsEmpty())
				portfolio.Board = exchangeInfoProvider.GetOrCreateBoard(message.BoardCode);

			if (!message.ClientCode.IsEmpty())
				portfolio.ClientCode = message.ClientCode;

			ApplyChanges(portfolio, message);
		}

		/// <summary>
		/// Apply changes to the position object.
		/// </summary>
		/// <param name="position">Position.</param>
		/// <param name="message">Position change message.</param>
		public static void ApplyChanges(this Position position, PositionChangeMessage message)
		{
			if (position == null)
				throw new ArgumentNullException(nameof(position));

			if (message == null)
				throw new ArgumentNullException(nameof(message));

			var pf = position as Portfolio ?? position.Portfolio;

			foreach (var change in message.Changes)
			{
				try
				{
					switch (change.Key)
					{
						case PositionChangeTypes.BeginValue:
							position.BeginValue = (decimal)change.Value;
							break;
						case PositionChangeTypes.CurrentValue:
							position.CurrentValue = (decimal)change.Value;
							break;
						case PositionChangeTypes.BlockedValue:
							position.BlockedValue = (decimal)change.Value;
							break;
						case PositionChangeTypes.CurrentPrice:
							position.CurrentPrice = (decimal)change.Value;
							break;
						case PositionChangeTypes.AveragePrice:
							position.AveragePrice = (decimal)change.Value;
							break;
						//case PositionChangeTypes.ExtensionInfo:
						//	var pair = change.Value.To<KeyValuePair<string, object>>();
						//	position.ExtensionInfo[pair.Key] = pair.Value;
						//	break;
						case PositionChangeTypes.RealizedPnL:
							position.RealizedPnL = (decimal)change.Value;
							break;
						case PositionChangeTypes.UnrealizedPnL:
							position.UnrealizedPnL = (decimal)change.Value;
							break;
						case PositionChangeTypes.Commission:
							position.Commission = (decimal)change.Value;
							break;
						case PositionChangeTypes.VariationMargin:
							position.VariationMargin = (decimal)change.Value;
							break;
						//case PositionChangeTypes.DepoName:
						//	position.ExtensionInfo[nameof(PositionChangeTypes.DepoName)] = change.Value;
						//	break;
						case PositionChangeTypes.Currency:
							position.Currency = (CurrencyTypes)change.Value;
							break;
						case PositionChangeTypes.ExpirationDate:
							position.ExpirationDate = (DateTimeOffset)change.Value;
							break;
						case PositionChangeTypes.SettlementPrice:
							position.SettlementPrice = (decimal)change.Value;
							break;
						case PositionChangeTypes.Leverage:
							position.Leverage = (decimal)change.Value;
							break;
						case PositionChangeTypes.State:
							if (pf != null)
								pf.State = (PortfolioStates)change.Value;
							break;
						case PositionChangeTypes.CommissionMaker:
							position.CommissionMaker = (decimal)change.Value;
							break;
						case PositionChangeTypes.CommissionTaker:
							position.CommissionTaker = (decimal)change.Value;
							break;
						case PositionChangeTypes.BuyOrdersCount:
							position.BuyOrdersCount = (int)change.Value;
							break;
						case PositionChangeTypes.SellOrdersCount:
							position.SellOrdersCount = (int)change.Value;
							break;
						case PositionChangeTypes.BuyOrdersMargin:
							position.BuyOrdersMargin = (decimal)change.Value;
							break;
						case PositionChangeTypes.SellOrdersMargin:
							position.SellOrdersMargin = (decimal)change.Value;
							break;
						case PositionChangeTypes.OrdersMargin:
							position.OrdersMargin = (decimal)change.Value;
							break;
						case PositionChangeTypes.OrdersCount:
							position.OrdersCount = (int)change.Value;
							break;
						case PositionChangeTypes.TradesCount:
							position.TradesCount = (int)change.Value;
							break;

						// skip unknown fields
						//default:
						//	throw new ArgumentOutOfRangeException(nameof(change), change.Key, LocalizedStrings.Str1219);
					}
				}
				catch (Exception ex)
				{
					throw new InvalidOperationException(LocalizedStrings.Str1220Params.Put(change.Key), ex);
				}
			}

			position.LocalTime = message.LocalTime;
			position.LastChangeTime = message.ServerTime;
			message.CopyExtensionInfo(position);
		}

		/// <summary>
		/// Apply change to the security object.
		/// </summary>
		/// <param name="security">Security.</param>
		/// <param name="changes">Changes.</param>
		/// <param name="serverTime">Change server time.</param>
		/// <param name="localTime">Local timestamp when a message was received/created.</param>
		/// <param name="defaultHandler">Default handler.</param>
		public static void ApplyChanges(this Security security, IEnumerable<KeyValuePair<Level1Fields, object>> changes, DateTimeOffset serverTime, DateTimeOffset localTime, Action<Security, Level1Fields, object> defaultHandler = null)
		{
			if (security == null)
				throw new ArgumentNullException(nameof(security));

			if (changes == null)
				throw new ArgumentNullException(nameof(changes));

			var bidChanged = false;
			var askChanged = false;
			var lastTradeChanged = false;
			var bestBid = security.BestBid ?? new QuoteChange();
			var bestAsk = security.BestAsk ?? new QuoteChange();

			var lastTrade = new Trade { Security = security };

			if (security.LastTrade != null)
			{
				lastTrade.Price = security.LastTrade.Price;
				lastTrade.Volume = security.LastTrade.Volume;
			}

			foreach (var pair in changes)
			{
				var value = pair.Value;

				try
				{
					switch (pair.Key)
					{
						case Level1Fields.OpenPrice:
							security.OpenPrice = (decimal)value;
							break;
						case Level1Fields.HighPrice:
							security.HighPrice = (decimal)value;
							break;
						case Level1Fields.LowPrice:
							security.LowPrice = (decimal)value;
							break;
						case Level1Fields.ClosePrice:
							security.ClosePrice = (decimal)value;
							break;
						case Level1Fields.StepPrice:
							security.StepPrice = (decimal)value;
							break;
						case Level1Fields.PriceStep:
							security.PriceStep = (decimal)value;
							break;
						case Level1Fields.Decimals:
							security.Decimals = (int)value;
							break;
						case Level1Fields.VolumeStep:
							security.VolumeStep = (decimal)value;
							break;
						case Level1Fields.Multiplier:
							security.Multiplier = (decimal)value;
							break;
						case Level1Fields.BestBidPrice:
							bestBid.Price = (decimal)value;
							bidChanged = true;
							break;
						case Level1Fields.BestBidVolume:
							bestBid.Volume = (decimal)value;
							bidChanged = true;
							break;
						case Level1Fields.BestAskPrice:
							bestAsk.Price = (decimal)value;
							askChanged = true;
							break;
						case Level1Fields.BestAskVolume:
							bestAsk.Volume = (decimal)value;
							askChanged = true;
							break;
						case Level1Fields.ImpliedVolatility:
							security.ImpliedVolatility = (decimal)value;
							break;
						case Level1Fields.HistoricalVolatility:
							security.HistoricalVolatility = (decimal)value;
							break;
						case Level1Fields.TheorPrice:
							security.TheorPrice = (decimal)value;
							break;
						case Level1Fields.Delta:
							security.Delta = (decimal)value;
							break;
						case Level1Fields.Gamma:
							security.Gamma = (decimal)value;
							break;
						case Level1Fields.Vega:
							security.Vega = (decimal)value;
							break;
						case Level1Fields.Theta:
							security.Theta = (decimal)value;
							break;
						case Level1Fields.Rho:
							security.Rho = (decimal)value;
							break;
						case Level1Fields.MarginBuy:
							security.MarginBuy = (decimal)value;
							break;
						case Level1Fields.MarginSell:
							security.MarginSell = (decimal)value;
							break;
						case Level1Fields.OpenInterest:
							security.OpenInterest = (decimal)value;
							break;
						case Level1Fields.MinPrice:
							security.MinPrice = (decimal)value;
							break;
						case Level1Fields.MaxPrice:
							security.MaxPrice = (decimal)value;
							break;
						case Level1Fields.BidsCount:
							security.BidsCount = (int)value;
							break;
						case Level1Fields.BidsVolume:
							security.BidsVolume = (decimal)value;
							break;
						case Level1Fields.AsksCount:
							security.AsksCount = (int)value;
							break;
						case Level1Fields.AsksVolume:
							security.AsksVolume = (decimal)value;
							break;
						case Level1Fields.State:
							security.State = (SecurityStates)value;
							break;
						case Level1Fields.LastTradePrice:
							lastTrade.Price = (decimal)value;
							lastTradeChanged = true;
							break;
						case Level1Fields.LastTradeVolume:
							lastTrade.Volume = (decimal)value;
							lastTradeChanged = true;
							break;
						case Level1Fields.LastTradeId:
							lastTrade.Id = (long)value;
							lastTradeChanged = true;
							break;
						case Level1Fields.LastTradeTime:
							lastTrade.Time = (DateTimeOffset)value;
							lastTradeChanged = true;
							break;
						case Level1Fields.LastTradeUpDown:
							lastTrade.IsUpTick = (bool)value;
							lastTradeChanged = true;
							break;
						case Level1Fields.LastTradeOrigin:
							lastTrade.OrderDirection = (Sides)value;
							lastTradeChanged = true;
							break;
						case Level1Fields.IsSystem:
							lastTrade.IsSystem = (bool)value;
							lastTradeChanged = true;
							break;
						case Level1Fields.TradesCount:
							security.TradesCount = (int)value;
							break;
						case Level1Fields.HighBidPrice:
							security.HighBidPrice = (decimal)value;
							break;
						case Level1Fields.LowAskPrice:
							security.LowAskPrice = (decimal)value;
							break;
						case Level1Fields.Yield:
							security.Yield = (decimal)value;
							break;
						case Level1Fields.VWAP:
							security.VWAP = (decimal)value;
							break;
						case Level1Fields.SettlementPrice:
							security.SettlementPrice = (decimal)value;
							break;
						case Level1Fields.AveragePrice:
							security.AveragePrice = (decimal)value;
							break;
						case Level1Fields.Volume:
							security.Volume = (decimal)value;
							break;
						case Level1Fields.Turnover:
							security.Turnover = (decimal)value;
							break;
						case Level1Fields.BuyBackPrice:
							security.BuyBackPrice = (decimal)value;
							break;
						case Level1Fields.BuyBackDate:
							security.BuyBackDate = (DateTimeOffset)value;
							break;
						case Level1Fields.CommissionTaker:
							security.CommissionTaker = (decimal)value;
							break;
						case Level1Fields.CommissionMaker:
							security.CommissionMaker = (decimal)value;
							break;
						case Level1Fields.MinVolume:
							security.MinVolume = (decimal)value;
							break;
						case Level1Fields.MaxVolume:
							security.MaxVolume = (decimal)value;
							break;
						case Level1Fields.UnderlyingMinVolume:
							security.UnderlyingSecurityMinVolume = (decimal)value;
							break;
						case Level1Fields.IssueSize:
							security.IssueSize = (decimal)value;
							break;
						default:
						{
							defaultHandler?.Invoke(security, pair.Key, pair.Value);
							break;
							//throw new ArgumentOutOfRangeException();
						}
					}
				}
				catch (Exception ex)
				{
					throw new InvalidOperationException(LocalizedStrings.Str1220Params.Put(pair.Key), ex);
				}
			}

			if (bidChanged)
				security.BestBid = bestBid;

			if (askChanged)
				security.BestAsk = bestAsk;

			if (lastTradeChanged)
			{
				if (lastTrade.Time.IsDefault())
					lastTrade.Time = serverTime;

				lastTrade.LocalTime = localTime;

				security.LastTrade = lastTrade;
			}

			security.LocalTime = localTime;
			security.LastChangeTime = serverTime;
		}

		/// <summary>
		/// Apply change to the security object.
		/// </summary>
		/// <param name="security">Security.</param>
		/// <param name="message">Changes.</param>
		public static void ApplyChanges(this Security security, Level1ChangeMessage message)
		{
			if (security == null)
				throw new ArgumentNullException(nameof(security));

			if (message == null)
				throw new ArgumentNullException(nameof(message));

			security.ApplyChanges(message.Changes, message.ServerTime, message.LocalTime);
		}

		/// <summary>
		/// Apply change to the security object.
		/// </summary>
		/// <param name="security">Security.</param>
		/// <param name="message">Meta info.</param>
		/// <param name="exchangeInfoProvider">Exchanges and trading boards provider.</param>
		/// <param name="isOverride">Override previous security data by new values.</param>
		public static void ApplyChanges(this Security security, SecurityMessage message, IExchangeInfoProvider exchangeInfoProvider, bool isOverride = true)
		{
			if (security == null)
				throw new ArgumentNullException(nameof(security));

			if (message == null)
				throw new ArgumentNullException(nameof(message));

			if (exchangeInfoProvider == null)
				throw new ArgumentNullException(nameof(exchangeInfoProvider));

			var secId = message.SecurityId;

			if (!secId.SecurityCode.IsEmpty())
			{
				if (isOverride || security.Code.IsEmpty())
					security.Code = secId.SecurityCode;
			}

			if (!secId.BoardCode.IsEmpty())
			{
				if (isOverride || security.Board == null)
					security.Board = exchangeInfoProvider.GetOrCreateBoard(secId.BoardCode);
			}

			if (message.Currency != null)
			{
				if (isOverride || security.Currency == null)
					security.Currency = message.Currency;
			}

			if (message.ExpiryDate != null)
			{
				if (isOverride || security.ExpiryDate == null)
					security.ExpiryDate = message.ExpiryDate;
			}

			if (message.VolumeStep != null)
			{
				if (isOverride || security.VolumeStep == null)
					security.VolumeStep = message.VolumeStep.Value;
			}

			if (message.MinVolume != null)
			{
				if (isOverride || security.MinVolume == null)
					security.MinVolume = message.MinVolume.Value;
			}

			if (message.MaxVolume != null)
			{
				if (isOverride || security.MaxVolume == null)
					security.MaxVolume = message.MaxVolume.Value;
			}

			if (message.Multiplier != null)
			{
				if (isOverride || security.Multiplier == null)
					security.Multiplier = message.Multiplier.Value;
			}

			if (message.PriceStep != null)
			{
				if (isOverride || security.PriceStep == null)
					security.PriceStep = message.PriceStep.Value;

				if (message.Decimals == null && security.Decimals == null)
					security.Decimals = message.PriceStep.Value.GetCachedDecimals();
			}

			if (message.Decimals != null)
			{
				if (isOverride || security.Decimals == null)
					security.Decimals = message.Decimals.Value;

				if (message.PriceStep == null && security.PriceStep == null)
					security.PriceStep = message.Decimals.Value.GetPriceStep();
			}

			if (!message.Name.IsEmpty())
			{
				if (isOverride || security.Name.IsEmpty())
					security.Name = message.Name;
			}

			if (!message.Class.IsEmpty())
			{
				if (isOverride || security.Class.IsEmpty())
					security.Class = message.Class;
			}

			if (message.OptionType != null)
			{
				if (isOverride || security.OptionType == null)
					security.OptionType = message.OptionType;
			}

			if (message.Strike != null)
			{
				if (isOverride || security.Strike == null)
					security.Strike = message.Strike.Value;
			}

			if (!message.BinaryOptionType.IsEmpty())
			{
				if (isOverride || security.BinaryOptionType == null)
					security.BinaryOptionType = message.BinaryOptionType;
			}

			if (message.SettlementDate != null)
			{
				if (isOverride || security.SettlementDate == null)
					security.SettlementDate = message.SettlementDate;
			}

			if (!message.ShortName.IsEmpty())
			{
				if (isOverride || security.ShortName.IsEmpty())
					security.ShortName = message.ShortName;
			}

			if (message.SecurityType != null)
			{
				if (isOverride || security.Type == null)
					security.Type = message.SecurityType.Value;
			}

			if (message.Shortable != null)
			{
				if (isOverride || security.Shortable == null)
					security.Shortable = message.Shortable.Value;
			}

			if (!message.CfiCode.IsEmpty())
			{
				if (isOverride || security.CfiCode.IsEmpty())
					security.CfiCode = message.CfiCode;

				if (security.Type == null)
					security.Type = security.CfiCode.Iso10962ToSecurityType();

				if (security.Type == SecurityTypes.Option && security.OptionType == null)
				{
					security.OptionType = security.CfiCode.Iso10962ToOptionType();

					//if (security.CfiCode.Length > 2)
					//	security.BinaryOptionType = security.CfiCode.Substring(2);
				}
			}

			if (!message.UnderlyingSecurityCode.IsEmpty())
			{
				if (isOverride || security.UnderlyingSecurityId.IsEmpty())
					security.UnderlyingSecurityId = message.UnderlyingSecurityCode + "@" + secId.BoardCode;
			}

			if (secId.HasExternalId())
			{
				if (isOverride || security.ExternalId.Equals(new SecurityExternalId()))
					security.ExternalId = secId.ToExternalId();
			}

			if (message.IssueDate != null)
			{
				if (isOverride || security.IssueDate == null)
					security.IssueDate = message.IssueDate.Value;
			}

			if (message.IssueSize != null)
			{
				if (isOverride || security.IssueSize == null)
					security.IssueSize = message.IssueSize.Value;
			}

			if (message.UnderlyingSecurityType != null)
			{
				if (isOverride || security.UnderlyingSecurityType == null)
					security.UnderlyingSecurityType = message.UnderlyingSecurityType.Value;
			}

			if (message.UnderlyingSecurityMinVolume != null)
			{
				if (isOverride || security.UnderlyingSecurityMinVolume == null)
					security.UnderlyingSecurityMinVolume = message.UnderlyingSecurityMinVolume.Value;
			}

			if (!message.BasketCode.IsEmpty())
			{
				if (isOverride || security.BasketCode.IsEmpty())
					security.BasketCode = message.BasketCode;
			}

			if (!message.BasketExpression.IsEmpty())
			{
				if (isOverride || security.BasketExpression.IsEmpty())
					security.BasketExpression = message.BasketExpression;
			}

			if (message.FaceValue != null)
			{
				if (isOverride || security.FaceValue == null)
					security.FaceValue = message.FaceValue;
			}

			if (message.PrimaryId != default)
			{
				if (isOverride || security.PrimaryId == default)
					security.PrimaryId = message.PrimaryId.ToStringId();
			}

			message.CopyExtensionInfo(security);
		}

		/// <summary>
		/// To get the instrument by the identifier.
		/// </summary>
		/// <param name="provider">The provider of information about instruments.</param>
		/// <param name="id">Security ID.</param>
		/// <returns>The got instrument. If there is no instrument by given criteria, <see langword="null" /> is returned.</returns>
		public static Security LookupById(this ISecurityProvider provider, string id)
		{
			return provider.LookupById(id.ToSecurityId());
		}

		/// <summary>
		/// Lookup securities by criteria <paramref name="criteria" />.
		/// </summary>
		/// <param name="provider">The provider of information about instruments.</param>
		/// <param name="criteria">The instrument whose fields will be used as a filter.</param>
		/// <returns>Found instruments.</returns>
		public static IEnumerable<Security> Lookup(this ISecurityProvider provider, Security criteria)
		{
			if (provider == null)
				throw new ArgumentNullException(nameof(provider));

			return provider.Lookup(criteria.ToLookupMessage());
		}

		/// <summary>
		/// To get the instrument by the system identifier.
		/// </summary>
		/// <param name="provider">The provider of information about instruments.</param>
		/// <param name="nativeIdStorage">Security native identifier storage.</param>
		/// <param name="storageName">Storage name.</param>
		/// <param name="nativeId">Native (internal) trading system security id.</param>
		/// <returns>The got instrument. If there is no instrument by given criteria, <see langword="null" /> is returned.</returns>
		public static Security LookupByNativeId(this ISecurityProvider provider, INativeIdStorage nativeIdStorage, string storageName, object nativeId)
		{
			if (provider == null)
				throw new ArgumentNullException(nameof(provider));

			if (nativeIdStorage == null)
				throw new ArgumentNullException(nameof(nativeIdStorage));

			if (nativeId == null)
				throw new ArgumentNullException(nameof(nativeId));

			var secId = nativeIdStorage.TryGetByNativeId(storageName, nativeId);

			return secId == null ? null : provider.LookupById(secId.Value);
		}

		/// <summary>
		/// To get the instrument by the instrument code.
		/// </summary>
		/// <param name="provider">The provider of information about instruments.</param>
		/// <param name="code">Security code.</param>
		/// <param name="type">Security type.</param>
		/// <returns>The got instrument. If there is no instrument by given criteria, <see langword="null" /> is returned.</returns>
		public static IEnumerable<Security> LookupByCode(this ISecurityProvider provider, string code, SecurityTypes? type = null)
		{
			if (provider == null)
				throw new ArgumentNullException(nameof(provider));

			return code.IsEmpty() && type == null
				? provider.LookupAll()
				: provider.Lookup(new Security { Code = code, Type = type });
		}

		/// <summary>
		/// Lookup all securities predefined criteria.
		/// </summary>
		public static readonly Security LookupAllCriteria = new Security();

		/// <summary>
		/// Determine the <paramref name="criteria"/> contains lookup all filter.
		/// </summary>
		/// <param name="criteria">The instrument whose fields will be used as a filter.</param>
		/// <returns>Check result.</returns>
		public static bool IsLookupAll(this Security criteria)
		{
			if (criteria == null)
				throw new ArgumentNullException(nameof(criteria));

			if (criteria == LookupAllCriteria)
				return true;

			return criteria.ToLookupMessage().IsLookupAll();
		}

		/// <summary>
		/// Get all available instruments.
		/// </summary>
		/// <param name="provider">The provider of information about instruments.</param>
		/// <returns>All available instruments.</returns>
		public static IEnumerable<Security> LookupAll(this ISecurityProvider provider)
		{
			if (provider == null)
				throw new ArgumentNullException(nameof(provider));

			return provider.Lookup(LookupAllCriteria);
		}

		/// <summary>
		/// Get or create (if not exist).
		/// </summary>
		/// <param name="storage">Securities meta info storage.</param>
		/// <param name="id">Security ID.</param>
		/// <param name="creator">Creator.</param>
		/// <param name="isNew">Is newly created.</param>
		/// <returns>Security.</returns>
		public static Security GetOrCreate(this ISecurityStorage storage, SecurityId id, Func<string, Security> creator, out bool isNew)
		{
			if (storage is null)
				throw new ArgumentNullException(nameof(storage));

			if (id == default)
				throw new ArgumentNullException(nameof(storage));

			if (creator is null)
				throw new ArgumentNullException(nameof(creator));

			lock (storage.SyncRoot)
			{
				var security = storage.LookupById(id);

				if (security == null)
				{
					security = creator(id.ToStringId());
					storage.Save(security, false);
					isNew = true;
				}
				else
					isNew = false;

				return security;
			}
		}

		/// <summary>
		/// Get or create (if not exist).
		/// </summary>
		/// <param name="storage">Storage.</param>
		/// <param name="portfolioName">Portfolio code name.</param>
		/// <param name="creator">Creator.</param>
		/// <param name="isNew">Is newly created.</param>
		/// <returns>Portfolio.</returns>
		public static Portfolio GetOrCreatePortfolio(this IPositionStorage storage, string portfolioName, Func<string, Portfolio> creator, out bool isNew)
		{
			if (storage is null)
				throw new ArgumentNullException(nameof(storage));

			if (creator is null)
				throw new ArgumentNullException(nameof(creator));

			lock (storage.SyncRoot)
			{
				var portfolio = storage.LookupByPortfolioName(portfolioName);

				if (portfolio == null)
				{
					portfolio = creator(portfolioName);
					storage.Save(portfolio);
					isNew = true;
				}
				else
					isNew = false;

				return portfolio;
			}
		}

		/// <summary>
		/// Get or create (if not exist).
		/// </summary>
		/// <param name="storage">Storage.</param>
		/// <param name="portfolio">Portfolio.</param>
		/// <param name="security">Security.</param>
		/// <param name="strategyId">Strategy ID.</param>
		/// <param name="clientCode">Client code.</param>
		/// <param name="depoName">Depo name.</param>
		/// <param name="limitType">Limit type.</param>
		/// <param name="creator">Creator.</param>
		/// <param name="isNew">Is newly created.</param>
		/// <returns>Position.</returns>
		public static Position GetOrCreatePosition(this IPositionStorage storage, Portfolio portfolio, Security security, string strategyId, string clientCode, string depoName, TPlusLimits? limitType, Func<Portfolio, Security, string, string, string, TPlusLimits?, Position> creator, out bool isNew)
		{
			if (storage is null)
				throw new ArgumentNullException(nameof(storage));

			if (portfolio is null)
				throw new ArgumentNullException(nameof(portfolio));

			if (security is null)
				throw new ArgumentNullException(nameof(security));

			if (creator is null)
				throw new ArgumentNullException(nameof(creator));

			lock (storage.SyncRoot)
			{
				var position = storage.GetPosition(portfolio, security, strategyId, clientCode, depoName, limitType);

				if (position == null)
				{
					position = creator(portfolio, security, strategyId, clientCode, depoName, limitType);
					storage.Save(position);
					isNew = true;
				}
				else
					isNew = false;

				return position;
			}
		}

		/// <summary>
		/// To delete all instruments.
		/// </summary>
		/// <param name="storage">Securities meta info storage.</param>
		public static void DeleteAll(this ISecurityStorage storage)
		{
			if (storage is null)
				throw new ArgumentNullException(nameof(storage));

			storage.DeleteBy(Extensions.LookupAllCriteriaMessage);
		}

		/// <summary>
		/// To get the value of market data for the instrument.
		/// </summary>
		/// <typeparam name="T">The type of the market data field value.</typeparam>
		/// <param name="provider">The market data provider.</param>
		/// <param name="security">Security.</param>
		/// <param name="field">Market-data field.</param>
		/// <returns>The field value. If no data, the <see langword="null" /> will be returned.</returns>
		public static T GetSecurityValue<T>(this IMarketDataProvider provider, Security security, Level1Fields field)
		{
			if (provider == null)
				throw new ArgumentNullException(nameof(provider));

			if (security == null)
				throw new ArgumentNullException(nameof(security));

			return (T)provider.GetSecurityValue(security, field);
		}

		/// <summary>
		/// To get all market data values for the instrument.
		/// </summary>
		/// <param name="provider">The market data provider.</param>
		/// <param name="security">Security.</param>
		/// <returns>Filed values. If there is no data, <see langword="null" /> is returned.</returns>
		public static IDictionary<Level1Fields, object> GetSecurityValues(this IMarketDataProvider provider, Security security)
		{
			if (provider == null)
				throw new ArgumentNullException(nameof(provider));

			if (security == null)
				throw new ArgumentNullException(nameof(security));

			var fields = provider.GetLevel1Fields(security).ToArray();

			if (fields.IsEmpty())
				return null;

			return fields.ToDictionary(f => f, f => provider.GetSecurityValue(security, f));
		}

		/// <summary>
		/// To get the type for the instrument in the ISO 10962 standard.
		/// </summary>
		/// <param name="security">Security.</param>
		/// <returns>Type in ISO 10962 standard.</returns>
		public static string Iso10962(this SecurityMessage security)
		{
			if (security == null)
				throw new ArgumentNullException(nameof(security));

			// https://en.wikipedia.org/wiki/ISO_10962

			switch (security.SecurityType)
			{
				case SecurityTypes.Stock:
					return "ESXXXX";
				case SecurityTypes.Future:
					return "FFXXXX";
				case SecurityTypes.Option:
				{
					switch (security.OptionType)
					{
						case OptionTypes.Call:
							return "OCXXXX";
						case OptionTypes.Put:
							return "OPXXXX";
						case null:
							return "OXXXXX";
						default:
							throw new ArgumentOutOfRangeException();
					}
				}
				case SecurityTypes.Index:
					return "MRIXXX";
				case SecurityTypes.Currency:
					return "MRCXXX";
				case SecurityTypes.Bond:
					return "DBXXXX";
				case SecurityTypes.Warrant:
					return "RWXXXX";
				case SecurityTypes.Forward:
					return "FFMXXX";
				case SecurityTypes.Swap:
					return "FFWXXX";
				case SecurityTypes.Commodity:
					return "MRTXXX";
				case SecurityTypes.Cfd:
					return "MMCXXX";
				case SecurityTypes.Adr:
					return "MMAXXX";
				case SecurityTypes.News:
					return "MMNXXX";
				case SecurityTypes.Weather:
					return "MMWXXX";
				case SecurityTypes.Fund:
					return "EUXXXX";
				case SecurityTypes.CryptoCurrency:
					return "MMBXXX";
				case null:
					return "XXXXXX";
				default:
					throw new ArgumentOutOfRangeException(nameof(security), security.SecurityType, LocalizedStrings.Str1219);
			}
		}

		/// <summary>
		/// To convert the type in the ISO 10962 standard into <see cref="SecurityTypes"/>.
		/// </summary>
		/// <param name="cfi">Type in ISO 10962 standard.</param>
		/// <returns>Security type.</returns>
		public static SecurityTypes? Iso10962ToSecurityType(this string cfi)
		{
			if (cfi.IsEmpty())
			{
				return null;
				//throw new ArgumentNullException(nameof(cfi));
			}

			if (cfi.Length != 6)
			{
				return null;
				//throw new ArgumentOutOfRangeException(nameof(cfi), cfi, LocalizedStrings.Str2117);
			}

			switch (cfi[0])
			{
				case 'E':
					return SecurityTypes.Stock;

				case 'D':
					return SecurityTypes.Bond;

				case 'R':
					return SecurityTypes.Warrant;

				case 'O':
					return SecurityTypes.Option;

				case 'F':
				{
					switch (cfi[2])
					{
						case 'W':
							return SecurityTypes.Swap;

						case 'M':
							return SecurityTypes.Forward;

						default:
							return SecurityTypes.Future;
					}
				}

				case 'M':
				{
					switch (cfi[1])
					{
						case 'R':
						{
							switch (cfi[2])
							{
								case 'I':
									return SecurityTypes.Index;

								case 'C':
									return SecurityTypes.Currency;

								case 'R':
									return SecurityTypes.Currency;

								case 'T':
									return SecurityTypes.Commodity;
							}

							break;
						}

						case 'M':
						{
							switch (cfi[2])
							{
								case 'B':
									return SecurityTypes.CryptoCurrency;

								case 'W':
									return SecurityTypes.Weather;

								case 'A':
									return SecurityTypes.Adr;

								case 'C':
									return SecurityTypes.Cfd;

								case 'N':
									return SecurityTypes.News;
							}

							break;
						}
					}

					break;
				}
			}

			return null;
		}

		/// <summary>
		/// To convert the type in the ISO 10962 standard into <see cref="OptionTypes"/>.
		/// </summary>
		/// <param name="cfi">Type in ISO 10962 standard.</param>
		/// <returns>Option type.</returns>
		public static OptionTypes? Iso10962ToOptionType(this string cfi)
		{
			if (cfi.IsEmpty())
				throw new ArgumentNullException(nameof(cfi));

			if (cfi[0] != 'O')
				return null;
				//throw new ArgumentOutOfRangeException(nameof(cfi), LocalizedStrings.Str1604Params.Put(cfi));

			if (cfi.Length < 2)
				throw new ArgumentOutOfRangeException(nameof(cfi), LocalizedStrings.Str1605Params.Put(cfi));

			switch (cfi[1])
			{
				case 'C':
					return OptionTypes.Call;
				case 'P':
					return OptionTypes.Put;
				case 'X':
				case ' ':
					return null;
				default:
					throw new ArgumentOutOfRangeException(nameof(cfi), LocalizedStrings.Str1606Params.Put(cfi));
			}
		}

		/// <summary>
		/// To get the number of operations, or discard the exception, if no information available.
		/// </summary>
		/// <param name="message">Operations.</param>
		/// <returns>Quantity.</returns>
		public static decimal SafeGetVolume(this ExecutionMessage message)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));

			var volume = message.OrderVolume ?? message.TradeVolume;

			if (volume != null)
				return volume.Value;

			var errorMsg = message.ExecutionType == ExecutionTypes.Tick || message.HasTradeInfo()
				? LocalizedStrings.Str1022Params.Put((object)message.TradeId ?? message.TradeStringId)
				: LocalizedStrings.Str927Params.Put((object)message.OrderId ?? message.OrderStringId);

			throw new ArgumentOutOfRangeException(nameof(message), null, errorMsg);
		}

		/// <summary>
		/// To get order identifier, or discard exception, if no information available.
		/// </summary>
		/// <param name="message">Operations.</param>
		/// <returns>Order ID.</returns>
		public static long SafeGetOrderId(this ExecutionMessage message)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));

			var orderId = message.OrderId;

			if (orderId != null)
				return orderId.Value;

			throw new ArgumentOutOfRangeException(nameof(message), null, LocalizedStrings.Str925);
		}

		/// <summary>
		/// To check the specified date is today.
		/// </summary>
		/// <param name="date">The specified date.</param>
		/// <returns><see langword="true"/> if the specified date is today, otherwise, <see langword="false"/>.</returns>
		public static bool IsToday(this DateTimeOffset date)
		{
			return date.DateTime == DateTime.Today;
		}

		///// <summary>
		///// To check the specified date is GTC.
		///// </summary>
		///// <param name="date">The specified date.</param>
		///// <returns><see langword="true"/> if the specified date is GTC, otherwise, <see langword="false"/>.</returns>
		//public static bool IsGtc(this DateTimeOffset date)
		//{
		//	return date == DateTimeOffset.MaxValue;
		//}

		/// <summary>
		/// Extract <see cref="TimeInForce"/> from bits flag.
		/// </summary>
		/// <param name="status">Bits flag.</param>
		/// <returns><see cref="TimeInForce"/>.</returns>
		public static TimeInForce? GetPlazaTimeInForce(this long status)
		{
			if (status.HasBits(0x1))
				return TimeInForce.PutInQueue;
			else if (status.HasBits(0x2))
				return TimeInForce.CancelBalance;
			else if (status.HasBits(0x80000))
				return TimeInForce.MatchOrCancel;

			return null;
		}

		/// <summary>
		/// Extract system attribute from the bits flag.
		/// </summary>
		/// <param name="status">Bits flag.</param>
		/// <returns><see langword="true"/> if an order is system, otherwise, <see langword="false"/>.</returns>
		public static bool IsPlazaSystem(this long status)
		{
			return !status.HasBits(0x4);
		}

		/// <summary>
		/// Convert <see cref="DataType"/> to readable string.
		/// </summary>
		/// <param name="dt"><see cref="DataType"/> instance.</param>
		/// <returns>Readable string.</returns>
		public static string ToReadableString(this DataType dt)
		{
			if (dt == null)
				throw new ArgumentNullException(nameof(dt));

			var tf = (TimeSpan)dt.Arg;

			var str = string.Empty;

			if (tf.Days > 0)
				str += LocalizedStrings.Str2918Params.Put(tf.Days);

			if (tf.Hours > 0)
				str = (str + " " + LocalizedStrings.Str2919Params.Put(tf.Hours)).Trim();

			if (tf.Minutes > 0)
				str = (str + " " + LocalizedStrings.Str2920Params.Put(tf.Minutes)).Trim();

			if (tf.Seconds > 0)
				str = (str + " " + LocalizedStrings.Seconds.Put(tf.Seconds)).Trim();

			if (str.IsEmpty())
				str = LocalizedStrings.Ticks;

			return str;
		}

		/// <summary>
		/// To get a board by its code. If board with the passed name does not exist, then it will be created.
		/// </summary>
		/// <param name="exchangeInfoProvider">Exchanges and trading boards provider.</param>
		/// <param name="code">Board code.</param>
		/// <param name="createBoard">The handler creating a board, if it is not found. If the value is <see langword="null" />, then the board is created by default initialization.</param>
		/// <returns>Exchange board.</returns>
		public static ExchangeBoard GetOrCreateBoard(this IExchangeInfoProvider exchangeInfoProvider, string code, Func<string, ExchangeBoard> createBoard = null)
		{
			return exchangeInfoProvider.GetOrCreateBoard(code, out _, createBoard);
		}

		/// <summary>
		/// To get a board by its code. If board with the passed name does not exist, then it will be created.
		/// </summary>
		/// <param name="exchangeInfoProvider">Exchanges and trading boards provider.</param>
		/// <param name="code">Board code.</param>
		/// <param name="isNew">Is newly created.</param>
		/// <param name="createBoard">The handler creating a board, if it is not found. If the value is <see langword="null" />, then the board is created by default initialization.</param>
		/// <returns>Exchange board.</returns>
		public static ExchangeBoard GetOrCreateBoard(this IExchangeInfoProvider exchangeInfoProvider, string code, out bool isNew, Func<string, ExchangeBoard> createBoard = null)
		{
			if (exchangeInfoProvider == null)
				throw new ArgumentNullException(nameof(exchangeInfoProvider));

			if (code.IsEmpty())
				throw new ArgumentNullException(nameof(code));

			isNew = false;

			//if (code.CompareIgnoreCase("RTS"))
			//	return ExchangeBoard.Forts;

			var board = exchangeInfoProvider.GetExchangeBoard(code);

			if (board != null)
				return board;

			isNew = true;

			if (createBoard == null)
			{
				var exchange = exchangeInfoProvider.GetExchange(code);

				if (exchange == null)
				{
					exchange = new Exchange { Name = code };
					exchangeInfoProvider.Save(exchange);
				}

				board = new ExchangeBoard
				{
					Code = code,
					Exchange = exchange
				};
			}
			else
			{
				board = createBoard(code);

				if (exchangeInfoProvider.GetExchange(board.Exchange.Name) == null)
					exchangeInfoProvider.Save(board.Exchange);
			}

			exchangeInfoProvider.Save(board);

			return board;
		}

		/// <summary>
		/// Is MICEX board.
		/// </summary>
		/// <param name="board">Board to check.</param>
		/// <returns>Check result.</returns>
		public static bool IsMicex(this ExchangeBoard board)
		{
			if (board == null)
				throw new ArgumentNullException(nameof(board));

			return board.Exchange == Exchange.Moex && board != ExchangeBoard.Forts;
		}

		/// <summary>
		/// Is the UX exchange stock market board.
		/// </summary>
		/// <param name="board">Board to check.</param>
		/// <returns>Check result.</returns>
		public static bool IsUxStock(this ExchangeBoard board)
		{
			if (board == null)
				throw new ArgumentNullException(nameof(board));

			return board.Exchange == Exchange.Ux && board != ExchangeBoard.Ux;
		}

		/// <summary>
		/// "All securities" instance.
		/// </summary>
		public static Security AllSecurity { get; } = new Security
		{
			Id = SecurityId.All.ToStringId(),
			Code = SecurityId.AssociatedBoardCode,
			//Class = task.GetDisplayName(),
			Name = LocalizedStrings.Str2835,
			Board = ExchangeBoard.Associated,
		};

		/// <summary>
		/// "News" security instance.
		/// </summary>
		public static readonly Security NewsSecurity = new Security { Id = SecurityId.News.ToStringId() };

		/// <summary>
		/// "Money" security instance.
		/// </summary>
		public static readonly Security MoneySecurity = new Security { Id = SecurityId.Money.ToStringId() };

		/// <summary>
		/// Find <see cref="AllSecurity"/> instance in the specified provider.
		/// </summary>
		/// <param name="provider">The provider of information about instruments.</param>
		/// <returns>Found instance.</returns>
		public static Security GetAllSecurity(this ISecurityProvider provider)
		{
			return provider.LookupById(SecurityId.All);
		}

		/// <summary>
		/// Check if the specified security is <see cref="AllSecurity"/>.
		/// </summary>
		/// <param name="security">Security.</param>
		/// <returns><see langword="true"/>, if the specified security is <see cref="AllSecurity"/>, otherwise, <see langword="false"/>.</returns>
		public static bool IsAllSecurity(this Security security)
		{
			if (security == null)
				throw new ArgumentNullException(nameof(security));

			return security == AllSecurity || security.Id.CompareIgnoreCase(AllSecurity.Id);
		}

		/// <summary>
		/// To check the correctness of the entered identifier.
		/// </summary>
		/// <param name="id">Security ID.</param>
		/// <returns>An error message text, or <see langword="null" /> if no error.</returns>
		public static string ValidateId(ref string id)
		{
			// 
			// can be fixed via TraderHelper.SecurityIdToFolderName
			//
			//var invalidChars = Path.GetInvalidFileNameChars().Where(id.Contains).ToArray();
			//if (invalidChars.Any())
			//{
			//	return LocalizedStrings.Str1549Params
			//		.Put(id, invalidChars.Select(c => c.To<string>()).Join(", "));
			//}

			var firstIndex = id.IndexOf('@');

			if (firstIndex == -1)
			{
				id += "@ALL";
				//return LocalizedStrings.Str2926;
			}

			var lastIndex = id.LastIndexOf('@');

			//if (firstIndex != id.LastIndexOf('@'))
			//	return LocalizedStrings.Str1550;

			if (firstIndex != lastIndex)
				return null;

			if (firstIndex == 0)
				return LocalizedStrings.Str2923;
			else if (firstIndex == (id.Length - 1))
				return LocalizedStrings.Str2926;

			return null;
		}

		/// <summary>
		/// Convert depths to quotes.
		/// </summary>
		/// <param name="messages">Depths.</param>
		/// <returns>Quotes.</returns>
		public static IEnumerable<TimeQuoteChange> ToTimeQuotes(this IEnumerable<QuoteChangeMessage> messages)
		{
			if (messages == null)
				throw new ArgumentNullException(nameof(messages));

			return messages.SelectMany(d => d.ToTimeQuotes());
		}

		/// <summary>
		/// Convert depth to quotes.
		/// </summary>
		/// <param name="message">Depth.</param>
		/// <returns>Quotes.</returns>
		public static IEnumerable<TimeQuoteChange> ToTimeQuotes(this QuoteChangeMessage message)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));

			return message.Asks.Select(q => new TimeQuoteChange(Sides.Sell, q, message)).Concat(message.Bids.Select(q => new TimeQuoteChange(Sides.Buy, q, message))).OrderByDescending(q => q.Quote.Price);
		}

		/// <summary>
		/// Is specified security id associated with the board.
		/// </summary>
		/// <param name="securityId">Security ID.</param>
		/// <param name="board">Board info.</param>
		/// <returns><see langword="true" />, if associated, otherwise, <see langword="false"/>.</returns>
		public static bool IsAssociated(this SecurityId securityId, ExchangeBoard board)
		{
			if (board == null)
				throw new ArgumentNullException(nameof(board));

			return securityId.BoardCode.CompareIgnoreCase(board.Code);
		}

		/// <summary>
		/// Lookup securities, portfolios and orders.
		/// </summary>
		/// <param name="connector">The connection of interaction with trade systems.</param>
		/// <param name="offlineMode">Offline mode handling message.</param>
		public static void LookupAll(this Connector connector, MessageOfflineModes offlineMode = MessageOfflineModes.Cancel)
		{
			if (connector == null)
				throw new ArgumentNullException(nameof(connector));

#pragma warning disable CS0618 // Type or member is obsolete
			connector.LookupBoards(new ExchangeBoard(), offlineMode: offlineMode);
			connector.LookupSecurities(LookupAllCriteria, offlineMode: offlineMode);
			connector.LookupPortfolios(new Portfolio(), offlineMode: offlineMode);
			connector.LookupOrders(new Order(), offlineMode: offlineMode);
#pragma warning restore CS0618 // Type or member is obsolete
		}

		/// <summary>
		/// Truncate the specified order book by max depth value.
		/// </summary>
		/// <param name="depth">Order book.</param>
		/// <param name="maxDepth">The maximum depth of order book.</param>
		/// <returns>Truncated order book.</returns>
		public static MarketDepth Truncate(this MarketDepth depth, int maxDepth)
		{
			if (depth == null)
				throw new ArgumentNullException(nameof(depth));

			var result = depth.Clone();
			result.Update(result.Bids2.Take(maxDepth).ToArray(), result.Asks2.Take(maxDepth).ToArray(), depth.LastChangeTime);
			return result;
		}

		/// <summary>
		/// Get adapter by portfolio.
		/// </summary>
		/// <param name="portfolioProvider">The portfolio based message adapter's provider.</param>
		/// <param name="adapterProvider">The message adapter's provider.</param>
		/// <param name="portfolio">Portfolio.</param>
		/// <returns>Found adapter or <see langword="null"/>.</returns>
		public static IMessageAdapter TryGetAdapter(this IPortfolioMessageAdapterProvider portfolioProvider, IMessageAdapterProvider adapterProvider, Portfolio portfolio)
		{
			if (portfolioProvider == null)
				throw new ArgumentNullException(nameof(portfolioProvider));

			if (adapterProvider == null)
				throw new ArgumentNullException(nameof(adapterProvider));

			if (portfolio == null)
				throw new ArgumentNullException(nameof(portfolio));

			return portfolioProvider.TryGetAdapter(adapterProvider.CurrentAdapters, portfolio);
		}

		/// <summary>
		/// Get adapter by portfolio.
		/// </summary>
		/// <param name="portfolioProvider">The portfolio based message adapter's provider.</param>
		/// <param name="adapters">All available adapters.</param>
		/// <param name="portfolio">Portfolio.</param>
		/// <returns>Found adapter or <see langword="null"/>.</returns>
		public static IMessageAdapter TryGetAdapter(this IPortfolioMessageAdapterProvider portfolioProvider, IEnumerable<IMessageAdapter> adapters, Portfolio portfolio)
		{
			if (portfolioProvider == null)
				throw new ArgumentNullException(nameof(portfolioProvider));

			if (adapters == null)
				throw new ArgumentNullException(nameof(adapters));

			if (portfolio == null)
				throw new ArgumentNullException(nameof(portfolio));

			var id = portfolioProvider.TryGetAdapter(portfolio.Name);

			if (id == null)
				return null;

			return adapters.FindById(id.Value);
		}

		/// <summary>
		/// Convert inner securities messages to basket.
		/// </summary>
		/// <typeparam name="TMessage">Message type.</typeparam>
		/// <param name="innerSecMessages">Inner securities messages.</param>
		/// <param name="security">Basket security.</param>
		/// <param name="processorProvider">Basket security processors provider.</param>
		/// <returns>Messages of basket securities.</returns>
		public static IEnumerable<TMessage> ToBasket<TMessage>(this IEnumerable<TMessage> innerSecMessages, Security security, IBasketSecurityProcessorProvider processorProvider)
			where TMessage : Message
		{
			var processor = processorProvider.CreateProcessor(security);

			return innerSecMessages.SelectMany(processor.Process).Cast<TMessage>();
		}

		/// <summary>
		/// Create market data processor for basket securities.
		/// </summary>
		/// <param name="processorProvider">Basket security processors provider.</param>
		/// <param name="security">Basket security.</param>
		/// <returns>Market data processor for basket securities.</returns>
		public static IBasketSecurityProcessor CreateProcessor(this IBasketSecurityProcessorProvider processorProvider, Security security)
		{
			if (processorProvider == null)
				throw new ArgumentNullException(nameof(processorProvider));

			if (security == null)
				throw new ArgumentNullException(nameof(security));

			return processorProvider.GetProcessorType(security.BasketCode).CreateInstance<IBasketSecurityProcessor>(security);
		}

		/// <summary>
		/// Is specified security is basket.
		/// </summary>
		/// <param name="security">Security.</param>
		/// <returns>Check result.</returns>
		public static bool IsBasket(this Security security)
		{
			if (security == null)
				throw new ArgumentNullException(nameof(security));

			return !security.BasketCode.IsEmpty();
		}

		/// <summary>
		/// Is specified security is basket.
		/// </summary>
		/// <param name="security">Security.</param>
		/// <returns>Check result.</returns>
		public static bool IsBasket(this SecurityMessage security)
		{
			if (security == null)
				throw new ArgumentNullException(nameof(security));

			return !security.BasketCode.IsEmpty();
		}
		
		/// <summary>
		/// Is specified security is index.
		/// </summary>
		/// <param name="security">Security.</param>
		/// <returns>Check result.</returns>
		public static bool IsIndex(this Security security)
		{
			if (security == null)
				throw new ArgumentNullException(nameof(security));

			return security.BasketCode == "WI" || security.BasketCode == "EI";
		}

		/// <summary>
		/// Is specified security is index.
		/// </summary>
		/// <param name="security">Security.</param>
		/// <returns>Check result.</returns>
		public static bool IsIndex(this SecurityMessage security)
		{
			if (security == null)
				throw new ArgumentNullException(nameof(security));

			return security.BasketCode == "WI" || security.BasketCode == "EI";
		}

		/// <summary>
		/// Is specified security is continuous.
		/// </summary>
		/// <param name="security">Security.</param>
		/// <returns>Check result.</returns>
		public static bool IsContinuous(this Security security)
		{
			if (security == null)
				throw new ArgumentNullException(nameof(security));

			return security.BasketCode == "CE" || security.BasketCode == "CV";
		}

		/// <summary>
		/// Is specified security is continuous.
		/// </summary>
		/// <param name="security">Security.</param>
		/// <returns>Check result.</returns>
		public static bool IsContinuous(this SecurityMessage security)
		{
			if (security == null)
				throw new ArgumentNullException(nameof(security));

			return security.BasketCode == "CE" || security.BasketCode == "CV";
		}

		/// <summary>
		/// Convert <see cref="Security"/> to <see cref="BasketSecurity"/> value.
		/// </summary>
		/// <param name="security">Security.</param>
		/// <param name="processorProvider">Basket security processors provider.</param>
		/// <returns>Instruments basket.</returns>
		public static BasketSecurity ToBasket(this Security security, IBasketSecurityProcessorProvider processorProvider)
		{
			if (security == null)
				throw new ArgumentNullException(nameof(security));

			if (processorProvider == null)
				throw new ArgumentNullException(nameof(processorProvider));

			var type = processorProvider.GetSecurityType(security.BasketCode);
			var basketSec = type.CreateInstance<BasketSecurity>();
			security.CopyTo(basketSec);
			return basketSec;
		}

		/// <summary>
		/// Convert <see cref="Security"/> to <see cref="BasketSecurity"/> value.
		/// </summary>
		/// <param name="security">Security.</param>
		/// <returns>Instruments basket.</returns>
		/// <typeparam name="TBasketSecurity">Basket security type.</typeparam>
		public static TBasketSecurity ToBasket<TBasketSecurity>(this Security security)
			where TBasketSecurity : BasketSecurity, new()
		{
			if (security == null)
				throw new ArgumentNullException(nameof(security));

			var basketSec = new TBasketSecurity();
			security.CopyTo(basketSec);
			return basketSec;
		}

		/// <summary>
		/// Filter boards by code criteria.
		/// </summary>
		/// <param name="provider">The exchange boards provider.</param>
		/// <param name="criteria">Criteria.</param>
		/// <returns>Found boards.</returns>
		public static IEnumerable<ExchangeBoard> LookupBoards(this IExchangeInfoProvider provider, BoardLookupMessage criteria)
		{
			if (provider == null)
				throw new ArgumentNullException(nameof(provider));

			return provider.Boards.Filter(criteria);
		}

		/// <summary>
		/// Filter boards by code criteria.
		/// </summary>
		/// <param name="boards">All boards.</param>
		/// <param name="criteria">Criteria.</param>
		/// <returns>Found boards.</returns>
		public static IEnumerable<ExchangeBoard> Filter(this IEnumerable<ExchangeBoard> boards, BoardLookupMessage criteria)
			=> boards.Where(b => b.ToMessage().IsMatch(criteria));

		/// <summary>
		/// Filter portfolios by the specified criteria.
		/// </summary>
		/// <param name="portfolios">All portfolios.</param>
		/// <param name="criteria">Criteria.</param>
		/// <returns>Found portfolios.</returns>
		public static IEnumerable<Portfolio> Filter(this IEnumerable<Portfolio> portfolios, PortfolioLookupMessage criteria)
			=> portfolios.Where(p => p.ToMessage().IsMatch(criteria, false));

		/// <summary>
		/// Filter positions the specified criteria.
		/// </summary>
		/// <param name="positions">All positions.</param>
		/// <param name="criteria">Criteria.</param>
		/// <returns>Found positions.</returns>
		public static IEnumerable<Position> Filter(this IEnumerable<Position> positions, PortfolioLookupMessage criteria)
			=> positions.Where(p => p.ToChangeMessage().IsMatch(criteria, false));

		/// <summary>
		/// Reregister the order.
		/// </summary>
		/// <param name="provider">The transactional provider.</param>
		/// <param name="oldOrder">Changing order.</param>
		/// <param name="price">Price of the new order.</param>
		/// <param name="volume">Volume of the new order.</param>
		/// <returns>New order.</returns>
		public static Order ReRegisterOrder(this ITransactionProvider provider, Order oldOrder, decimal price, decimal volume)
		{
			if (provider == null)
				throw new ArgumentNullException(nameof(provider));

			var newOrder = oldOrder.ReRegisterClone(price, volume);
			provider.ReRegisterOrder(oldOrder, newOrder);
			return newOrder;
		}

		private static void DoConnect(this IMessageAdapter adapter, IEnumerable<Message> requests, bool waitResponse, Func<Message, Tuple<bool, Exception>> newMessage)
		{
			if (adapter is null)
				throw new ArgumentNullException(nameof(adapter));

			if (requests is null)
				throw new ArgumentNullException(nameof(requests));

			if (newMessage is null)
				throw new ArgumentNullException(nameof(newMessage));

			if (adapter.IsNativeIdentifiers && !adapter.StorageName.IsEmpty())
			{
				var nativeIdAdapter = adapter.FindAdapter<SecurityNativeIdMessageAdapter>();
				
				if (nativeIdAdapter != null)
				{
					foreach (var secIdMsg in requests.OfType<ISecurityIdMessage>())
					{
						var secId = secIdMsg.SecurityId;

						if (secId == default)
							continue;

						var native = nativeIdAdapter.Storage.TryGetBySecurityId(adapter.StorageName, secId);
						secId.Native = native;
						secIdMsg.SecurityId = secId;
					}
				}
			}

			var sync = new SyncObject();
			
			adapter.NewOutMessage += msg =>
			{
				if (msg is BaseConnectionMessage conMsg)
					sync.PulseSignal(conMsg.Error);
				else
				{
					var tuple = newMessage(msg);

					if (tuple != null)
						sync.PulseSignal(tuple.Item2);
				}
			};

			CultureInfo.InvariantCulture.DoInCulture(() =>
			{
				adapter.SendInMessage(new ConnectMessage());

				lock (sync)
				{
					if (!sync.WaitSignal(adapter.ReConnectionSettings.TimeOutInterval, out var error))
						throw new TimeoutException();

					if (error != null)
						throw new InvalidOperationException(LocalizedStrings.Str2959, (Exception)error);
				}

				foreach (var request in requests)
				{
					if (request is ITransactionIdMessage transIdMsg && transIdMsg.TransactionId == 0)
						transIdMsg.TransactionId = adapter.TransactionIdGenerator.GetNextId();

					adapter.SendInMessage(request);
				}

				if (waitResponse)
				{
					lock (sync)
					{
						if (!sync.WaitSignal(TimeSpan.FromMinutes(10), out var error))
							throw new TimeoutException("Processing too long.");

						if (error != null)
							throw new InvalidOperationException(LocalizedStrings.Str2955, (Exception)error);
					}
				}
				
				adapter.SendInMessage(new DisconnectMessage());
			});
		}

		/// <summary>
		/// Upload data.
		/// </summary>
		/// <typeparam name="TMessage">Request type.</typeparam>
		/// <param name="adapter">Adapter.</param>
		/// <param name="messages">Messages.</param>
		public static void Upload<TMessage>(this IMessageAdapter adapter, IEnumerable<TMessage> messages)
			where TMessage : Message
		{
			adapter.DoConnect(messages,	false, msg => null);
		}

		/// <summary>
		/// Download data.
		/// </summary>
		/// <typeparam name="TResult">Result message.</typeparam>
		/// <param name="adapter">Adapter.</param>
		/// <param name="request">Request.</param>
		/// <returns>Downloaded data.</returns>
		public static IEnumerable<TResult> Download<TResult>(this IMessageAdapter adapter, Message request)
			where TResult : Message, IOriginalTransactionIdMessage
		{
			var retVal = new List<TResult>();

			var transIdMsg = request as ITransactionIdMessage;

			adapter.DoConnect(new[] { request }, true,
				msg =>
				{
					if (transIdMsg != null && msg is IOriginalTransactionIdMessage origIdMsg)
					{
						if (origIdMsg.OriginalTransactionId == transIdMsg.TransactionId)
						{
							if (msg is TResult resMsg)
								retVal.Add(resMsg);
							else if (msg is SubscriptionResponseMessage responseMsg && responseMsg.Error != null)
								return Tuple.Create(true, responseMsg.Error);
							else if (msg is ErrorMessage errorMsg)
								return Tuple.Create(true, errorMsg.Error);
							else if (msg is SubscriptionFinishedMessage)
								return Tuple.Create(true, (Exception)null);
						}
					}

					return null;
				});

			return retVal;
		}

		/// <summary>
		/// To get level1 market data.
		/// </summary>
		/// <param name="adapter">Adapter.</param>
		/// <param name="securityId">Security ID.</param>
		/// <param name="beginDate">Start date.</param>
		/// <param name="endDate">End date.</param>
		/// <param name="fields">Market data fields.</param>
		/// <returns>Level1 market data.</returns>
		public static IEnumerable<Level1ChangeMessage> GetLevel1(this IMessageAdapter adapter, SecurityId securityId, DateTime beginDate, DateTime endDate, IEnumerable<Level1Fields> fields = null)
		{
			var mdMsg = new MarketDataMessage
			{
				SecurityId = securityId,
				IsSubscribe = true,
				DataType2 = DataType.Level1,
				From = beginDate,
				To = endDate,
				BuildField = fields?.FirstOr(),
			};
			
			return adapter.Download<Level1ChangeMessage>(mdMsg);
		}

		/// <summary>
		/// To get tick data.
		/// </summary>
		/// <param name="adapter">Adapter.</param>
		/// <param name="securityId">Security ID.</param>
		/// <param name="beginDate">Start date.</param>
		/// <param name="endDate">End date.</param>
		/// <returns>Tick data.</returns>
		public static IEnumerable<ExecutionMessage> GetTicks(this IMessageAdapter adapter, SecurityId securityId, DateTime beginDate, DateTime endDate)
		{
			var mdMsg = new MarketDataMessage
			{
				SecurityId = securityId,
				IsSubscribe = true,
				DataType2 = DataType.Ticks,
				From = beginDate,
				To = endDate,
			};
			
			return adapter.Download<ExecutionMessage>(mdMsg);
		}

		/// <summary>
		/// To get order log.
		/// </summary>
		/// <param name="adapter">Adapter.</param>
		/// <param name="securityId">Security ID.</param>
		/// <param name="beginDate">Start date.</param>
		/// <param name="endDate">End date.</param>
		/// <returns>Order log.</returns>
		public static IEnumerable<ExecutionMessage> GetOrderLog(this IMessageAdapter adapter, SecurityId securityId, DateTime beginDate, DateTime endDate)
		{
			var mdMsg = new MarketDataMessage
			{
				SecurityId = securityId,
				IsSubscribe = true,
				DataType2 = DataType.OrderLog,
				From = beginDate,
				To = endDate,
			};
			
			return adapter.Download<ExecutionMessage>(mdMsg);
		}

		/// <summary>
		/// Download all securities.
		/// </summary>
		/// <param name="adapter">Adapter.</param>
		/// <param name="lookupMsg">Message security lookup for specified criteria.</param>
		/// <returns>All securities.</returns>
		public static IEnumerable<SecurityMessage> GetSecurities(this IMessageAdapter adapter, SecurityLookupMessage lookupMsg)
		{
			return adapter.Download<SecurityMessage>(lookupMsg);
		}

		/// <summary>
		/// To download candles.
		/// </summary>
		/// <param name="adapter">Adapter.</param>
		/// <param name="securityId">Security ID.</param>
		/// <param name="timeFrame">Time-frame.</param>
		/// <param name="from">Begin period.</param>
		/// <param name="to">End period.</param>
		/// <param name="count">Candles count.</param>
		/// <param name="buildField">Extra info for the <see cref="MarketDataMessage.BuildFrom"/>.</param>
		/// <returns>Downloaded candles.</returns>
		public static IEnumerable<TimeFrameCandleMessage> GetCandles(this IMessageAdapter adapter, SecurityId securityId, TimeSpan timeFrame, DateTimeOffset from, DateTimeOffset to, long? count = null, Level1Fields? buildField = null)
		{
			var mdMsg = new MarketDataMessage
			{
				SecurityId = securityId,
				IsSubscribe = true,
				DataType2 = DataType.TimeFrame(timeFrame),
				From = from,
				To = to,
				Count = count,
				BuildField = buildField,
			};

			return adapter.Download<TimeFrameCandleMessage>(mdMsg);
		}

		/// <summary>
		/// Get portfolio identifier.
		/// </summary>
		/// <param name="portfolio">Portfolio.</param>
		/// <returns>Portfolio identifier.</returns>
		public static string GetUniqueId(this Portfolio portfolio)
		{
			if (portfolio == null)
				throw new ArgumentNullException(nameof(portfolio));

			return /*portfolio.InternalId?.To<string>() ?? */portfolio.Name;
		}

		/// <summary>
		/// Determines the specified portfolio is required.
		/// </summary>
		/// <param name="portfolio">Portfolio.</param>
		/// <param name="uniqueId">Portfolio identifier.</param>
		/// <returns>Check result.</returns>
		public static bool IsSame(this Portfolio portfolio, string uniqueId)
		{
			if (portfolio == null)
				throw new ArgumentNullException(nameof(portfolio));

			return portfolio.Name.CompareIgnoreCase(uniqueId);// || (portfolio.InternalId != null && Guid.TryParse(uniqueId, out var indernalId) && portfolio.InternalId == indernalId);
		}

		/// <summary>
		/// Compile mathematical formula.
		/// </summary>
		/// <param name="expression">Text expression.</param>
		/// <param name="useIds">Use ids as variables.</param>
		/// <returns>Compiled mathematical formula.</returns>
		public static ExpressionFormula Compile(this string expression, bool useIds = true)
		{
			return ServicesRegistry.CompilerService.Compile(expression, useIds);
		}

		/// <summary>
		/// Create <see cref="IMessageAdapter"/>.
		/// </summary>
		/// <typeparam name="TAdapter">Adapter type.</typeparam>
		/// <param name="connector">The class to create connections to trading systems.</param>
		/// <param name="init">Initialize <typeparamref name="TAdapter"/>.</param>
		/// <returns>The class to create connections to trading systems.</returns>
		public static Connector AddAdapter<TAdapter>(this Connector connector, Action<TAdapter> init)
			where TAdapter : IMessageAdapter
		{
			if (connector == null)
				throw new ArgumentNullException(nameof(connector));

			if (init == null)
				throw new ArgumentNullException(nameof(init));

			var adapter = (TAdapter)typeof(TAdapter).CreateAdapter(connector.TransactionIdGenerator);
			init(adapter);
			connector.Adapter.InnerAdapters.Add(adapter);
			return connector;
		}

		/// <summary>
		/// Determines the specified state equals <see cref="SubscriptionStates.Active"/> or <see cref="SubscriptionStates.Online"/>.
		/// </summary>
		/// <param name="state">State.</param>
		/// <returns>Check result.</returns>
		public static bool IsActive(this SubscriptionStates state)
		{
			return state == SubscriptionStates.Active || state == SubscriptionStates.Online;
		}

		/// <summary>
		/// Determines whether the specified news related with StockSharp.
		/// </summary>
		/// <param name="news">News.</param>
		/// <returns>Check result.</returns>
		public static bool IsStockSharp(this News news)
		{
			if (news == null)
				throw new ArgumentNullException(nameof(news));

			return news.Source.CompareIgnoreCase(Messages.Extensions.NewsStockSharpSource);
		}

		/// <summary>
		/// Indicator value.
		/// </summary>
		public static DataType IndicatorValue { get; } = DataType.Create(typeof(Indicators.IIndicatorValue), null);//.Immutable();

		/// <summary>
		/// To determine whether the order book is in the right state.
		/// </summary>
		/// <param name="depth">Order book.</param>
		/// <returns><see langword="true" />, if the order book contains correct data, otherwise <see langword="false" />.</returns>
		/// <remarks>
		/// It is used in cases when the trading system by mistake sends the wrong quotes.
		/// </remarks>
		public static bool Verify(this MarketDepth depth)
		{
			return depth.ToMessage().Verify();
		}
	}
}