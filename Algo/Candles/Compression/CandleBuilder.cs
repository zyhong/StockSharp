#region S# License
/******************************************************************************************
NOTICE!!!  This program and source code is owned and licensed by
StockSharp, LLC, www.stocksharp.com
Viewing or use of this code requires your acceptance of the license
agreement found at https://github.com/StockSharp/StockSharp/blob/master/LICENSE
Removal of this comment is a violation of the license agreement.

Project: StockSharp.Algo.Candles.Compression.Algo
File: CandleBuilder.cs
Created: 2015, 11, 11, 2:32 PM

Copyright 2010 by StockSharp, LLC
*******************************************************************************************/
#endregion S# License
namespace StockSharp.Algo.Candles.Compression
{
	using System;
	using System.Collections.Generic;
	using System.Linq;

	using Ecng.Common;

	using StockSharp.Algo.Storages;
	using StockSharp.Logging;
	using StockSharp.Messages;
	using StockSharp.Localization;

	/// <summary>
	/// Candles builder.
	/// </summary>
	/// <typeparam name="TCandleMessage">The type of candle which the builder will create.</typeparam>
	public abstract class CandleBuilder<TCandleMessage> : BaseLogReceiver, ICandleBuilder
		where TCandleMessage : CandleMessage
	{
		/// <inheritdoc />
		public virtual Type CandleType { get; } = typeof(TCandleMessage);

		/// <summary>
		/// Initialize <see cref="CandleBuilder{TCandleMessage}"/>.
		/// </summary>
		/// <param name="exchangeInfoProvider">The exchange boards provider.</param>
		protected CandleBuilder(IExchangeInfoProvider exchangeInfoProvider)
		{
			ExchangeInfoProvider = exchangeInfoProvider ?? throw new ArgumentNullException(nameof(exchangeInfoProvider));
		}

		/// <summary>
		/// The exchange boards provider.
		/// </summary>
		protected IExchangeInfoProvider ExchangeInfoProvider { get; }

		/// <inheritdoc />
		public IEnumerable<CandleMessage> Process(MarketDataMessage message, CandleMessage currentCandle, ICandleBuilderValueTransform transform, ref VolumeProfileBuilder volumeProfile)
		{
			if (!IsTimeValid(message, transform.Time))
				return Enumerable.Empty<CandleMessage>();

			var changes = new List<CandleMessage>();

			Process(message, currentCandle, transform, ref volumeProfile, changes);

			return changes;
		}

		private bool IsTimeValid(MarketDataMessage message, DateTimeOffset time)
		{
			if (!message.IsRegularTradingHours)
				return true;

			var board = ExchangeInfoProvider.GetExchangeBoard(message.SecurityId.BoardCode);

			if (board == null)
				return true;

			return board.IsTradeTime(time, out _);
		}

		/// <summary>
		/// To process the new data.
		/// </summary>
		/// <param name="message">Market-data message (uses as a subscribe/unsubscribe in outgoing case, confirmation event in incoming case).</param>
		/// <param name="currentCandle">The current candle.</param>
		/// <param name="transform">The data source transformation.</param>
		/// <param name="volumeProfile">Volume profile.</param>
		/// <param name="changes">A new candles changes.</param>
		public virtual void Process(MarketDataMessage message, CandleMessage currentCandle, ICandleBuilderValueTransform transform, ref VolumeProfileBuilder volumeProfile, IList<CandleMessage> changes)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));

			if (transform == null)
				throw new ArgumentNullException(nameof(transform));

			var candle = ProcessValue(message, (TCandleMessage)currentCandle, transform);

			if (candle == null)
			{
				// skip the value that cannot be processed
				return;
			}

			if (candle == currentCandle)
			{
				if (message.IsCalcVolumeProfile)
				{
					if (volumeProfile == null)
						throw new InvalidOperationException();

					volumeProfile.Update(transform);
				}

				//candle.State = CandleStates.Changed;
				changes.Add(candle);
			}
			else
			{
				if (currentCandle != null)
				{
					currentCandle.State = CandleStates.Finished;
					changes.Add(currentCandle);
				}

				if (message.IsCalcVolumeProfile)
				{
					var levels = new List<CandlePriceLevel>();

					volumeProfile = new VolumeProfileBuilder(levels);
					volumeProfile.Update(transform);

					candle.PriceLevels = levels;
				}

				candle.State = CandleStates.Active;
				changes.Add(candle);
			}
		}

		/// <summary>
		/// To create a new candle.
		/// </summary>
		/// <param name="message">Market-data message (uses as a subscribe/unsubscribe in outgoing case, confirmation event in incoming case).</param>
		/// <param name="currentCandle">The current candle.</param>
		/// <param name="transform">The data source transformation.</param>
		/// <returns>Created candle.</returns>
		protected virtual TCandleMessage CreateCandle(MarketDataMessage message, TCandleMessage currentCandle, ICandleBuilderValueTransform transform)
			=> throw new NotSupportedException(LocalizedStrings.Str637);

		/// <summary>
		/// Whether the candle is created before data adding.
		/// </summary>
		/// <param name="message">Market-data message (uses as a subscribe/unsubscribe in outgoing case, confirmation event in incoming case).</param>
		/// <param name="candle">Candle.</param>
		/// <param name="transform">The data source transformation.</param>
		/// <returns><see langword="true" /> if the candle should be finished. Otherwise, <see langword="false" />.</returns>
		protected virtual bool IsCandleFinishedBeforeChange(MarketDataMessage message, TCandleMessage candle, ICandleBuilderValueTransform transform)
			=> false;

		/// <summary>
		/// To fill in the initial candle settings.
		/// </summary>
		/// <param name="message">Market-data message (uses as a subscribe/unsubscribe in outgoing case, confirmation event in incoming case).</param>
		/// <param name="candle">Candle.</param>
		/// <param name="transform">The data source transformation.</param>
		/// <returns>Candle.</returns>
		protected virtual TCandleMessage FirstInitCandle(MarketDataMessage message, TCandleMessage candle, ICandleBuilderValueTransform transform)
		{
			if (candle == null)
				throw new ArgumentNullException(nameof(candle));

			if (transform == null)
				throw new ArgumentNullException(nameof(transform));

			var price = transform.Price;
			var volume = transform.Volume;

			candle.BuildFrom = transform.BuildFrom;
			candle.SecurityId = message.SecurityId;

			candle.OpenPrice = price;
			candle.ClosePrice = price;
			candle.LowPrice = price;
			candle.HighPrice = price;
			//candle.TotalPrice = price;

			candle.OpenVolume = volume;
			candle.CloseVolume = volume;
			candle.LowVolume = volume;
			candle.HighVolume = volume;

			if (volume != null)
				candle.TotalVolume = volume.Value;

			candle.OpenInterest = transform.OpenInterest;
			candle.PriceLevels = transform.PriceLevels;

			candle.TotalTicks = 1;

			return candle;
		}

		/// <summary>
		/// To update the candle data.
		/// </summary>
		/// <param name="message">Market-data message (uses as a subscribe/unsubscribe in outgoing case, confirmation event in incoming case).</param>
		/// <param name="candle">Candle.</param>
		/// <param name="transform">The data source transformation.</param>
		protected virtual void UpdateCandle(MarketDataMessage message, TCandleMessage candle, ICandleBuilderValueTransform transform)
		{
			if (candle == null)
				throw new ArgumentNullException(nameof(candle));

			if (transform == null)
				throw new ArgumentNullException(nameof(transform));

			var price = transform.Price;
			var time = transform.Time;
			var volume = transform.Volume;

			if (price < candle.LowPrice)
			{
				candle.LowPrice = price;
				candle.LowTime = time;
				candle.LowVolume = volume;
			}

			if (price > candle.HighPrice)
			{
				candle.HighPrice = price;
				candle.HighTime = time;
				candle.HighVolume = volume;
			}

			candle.ClosePrice = price;

			if (volume != null)
			{
				var v = volume.Value;

				candle.TotalPrice += price * v;

				candle.CloseVolume = v;
				candle.TotalVolume += v;

				var dir = transform.Side;
				if (dir != null)
					candle.RelativeVolume = (candle.RelativeVolume ?? 0) + (dir.Value == Sides.Buy ? v : -v);
			}

			candle.CloseTime = time;

			candle.OpenInterest = transform.OpenInterest;

			if (transform.PriceLevels != null)
			{
				if (candle.PriceLevels == null)
					candle.PriceLevels = transform.PriceLevels;
				else
				{
					var dict = candle.PriceLevels.ToDictionary(l => l.Price);

					foreach (var level in transform.PriceLevels)
					{
						if (dict.TryGetValue(level.Price, out var currLevel))
						{
							currLevel.BuyCount += level.BuyCount;
							currLevel.SellCount += level.SellCount;
							currLevel.BuyVolume += level.BuyVolume;
							currLevel.SellVolume += level.SellVolume;
							currLevel.TotalVolume += level.TotalVolume;
							
							if (level.BuyVolumes != null)
							{
								if (currLevel.BuyVolumes == null)
									currLevel.BuyVolumes = level.BuyVolumes.ToArray();
								else
									currLevel.BuyVolumes = currLevel.BuyVolumes.Concat(level.BuyVolumes).ToArray();
							}

							if (currLevel.SellVolumes != null && level.SellVolumes != null)
							{
								if (currLevel.SellVolumes == null)
									currLevel.SellVolumes = level.SellVolumes.ToArray();
								else
									currLevel.SellVolumes = currLevel.SellVolumes.Concat(level.SellVolumes).ToArray();
							}
						}
						else
							dict.Add(level.Price, level);
					}

					candle.PriceLevels = dict.Values.ToArray();
				}
			}

			if (candle.TotalTicks != null)
				candle.TotalTicks++;
			else
				candle.TotalTicks = 1;
		}

		/// <summary>
		/// To process the new data.
		/// </summary>
		/// <param name="message">Market-data message (uses as a subscribe/unsubscribe in outgoing case, confirmation event in incoming case).</param>
		/// <param name="currentCandle">The current candle.</param>
		/// <param name="transform">The data source transformation.</param>
		/// <returns>A new candle. If there is not necessary to create a new candle, then <paramref name="currentCandle" /> is returned. If it is impossible to create a new candle (<paramref name="transform" /> cannot be applied to candles), then <see langword="null" /> is returned.</returns>
		protected virtual TCandleMessage ProcessValue(MarketDataMessage message, TCandleMessage currentCandle, ICandleBuilderValueTransform transform)
		{
			if (currentCandle == null || IsCandleFinishedBeforeChange(message, currentCandle, transform))
			{
				currentCandle = CreateCandle(message, currentCandle, transform);
				this.AddDebugLog("NewCandle {0} ForValue {1}", currentCandle, transform);
				return currentCandle;
			}

			UpdateCandle(message, currentCandle, transform);

			// TODO performance
			//this.AddDebugLog("UpdatedCandle {0} ForValue {1}", currentCandle, value);

			return currentCandle;
		}

		///// <summary>
		///// To finish the candle forcibly.
		///// </summary>
		///// <param name="message">Market-data message (uses as a subscribe/unsubscribe in outgoing case, confirmation event in incoming case).</param>
		///// <param name="candleMessage">Candle.</param>
		//protected void ForceFinishCandle(MarketDataMessage message, CandleMessage candleMessage)
		//{
		//	var info = _info.TryGetValue(message);

		//	if (info == null)
		//		return;

		//	var isNone = candleMessage.State == CandleStates.None;

		//	// если успела прийти новая свеча
		//	if (isNone && info.CurrentCandle != null)
		//		return;

		//	if (!isNone && info.CurrentCandle != candleMessage)
		//		return;

		//	info.CurrentCandle = isNone ? null : candleMessage;

		//	if (!isNone)
		//		candleMessage.State = CandleStates.Finished;

		//	RaiseProcessing(series, candleMessage);
		//}
	}

	/// <summary>
	/// The builder of candles of <see cref="TimeFrameCandleMessage"/> type.
	/// </summary>
	public class TimeFrameCandleBuilder : CandleBuilder<TimeFrameCandleMessage>
	{
		//private sealed class TimeoutInfo : Disposable
		//{
		//	private readonly MarketTimer _timer;
		//	private DateTime _emptyCandleTime;
		//	private readonly TimeSpan _timeFrame;
		//	private readonly TimeSpan _offset;
		//	private DateTime _nextTime;

		//	public TimeoutInfo(CandleSeries series, TimeFrameCandleBuilder builder)
		//	{
		//		if (series == null)
		//			throw new ArgumentNullException(nameof(series));

		//		if (builder == null)
		//			throw new ArgumentNullException(nameof(builder));

		//		_timeFrame = (TimeSpan)series.Arg;
		//		_offset = TimeSpan.FromTicks((long)((decimal)((decimal)_timeFrame.Ticks + builder.Timeout)));

		//		var security = series.Security;
		//		var connector = security.Connector;

		//		var isFirstTime = true;

		//		_timer = new MarketTimer(connector, () =>
		//		{
		//			if (isFirstTime)
		//			{
		//				isFirstTime = false;

		//				var bounds = _timeFrame.GetCandleBounds(security);

		//				_emptyCandleTime = bounds.Min;
		//				_nextTime = GetLimitTime(bounds.Min);

		//				return;
		//			}

		//			if (security.GetMarketTime() >= _nextTime)
		//			{
		//				_nextTime += _timeFrame;

		//				var candle = LastCandle;

		//				if (candle == null)
		//				{
		//					candle = new TimeFrameCandle
		//					{
		//						Security = security,
		//						TimeFrame = _timeFrame,
		//						OpenTime = _emptyCandleTime,
		//						CloseTime = _emptyCandleTime + _timeFrame,
		//					};

		//					_emptyCandleTime += _timeFrame;

		//					if (!builder.GenerateEmptyCandles)
		//						return;
		//				}

		//				builder.ForceFinishCandle(series, candle);
		//			}
		//		}).Interval(_timeFrame).Start();
		//	}

		//	private TimeFrameCandle _lastCandle;

		//	public TimeFrameCandle LastCandle
		//	{
		//		private get { return _lastCandle; }
		//		set
		//		{
		//			_lastCandle = value;
		//			_emptyCandleTime = value.OpenTime + _timeFrame;
		//			_nextTime = GetLimitTime(value.OpenTime);
		//		}
		//	}

		//	private DateTime GetLimitTime(DateTime currentCandleTime)
		//	{
		//		return currentCandleTime + _offset;
		//	}

		//	protected override void DisposeManaged()
		//	{
		//		base.DisposeManaged();
		//		_timer.Dispose();
		//	}
		//}

		//private readonly SynchronizedDictionary<CandleSeries, TimeoutInfo> _timeoutInfos = new SynchronizedDictionary<CandleSeries, TimeoutInfo>();

		/// <summary>
		/// Whether to create empty candles (<see cref="CandleStates.None"/>) in the lack of trades. The default mode is enabled.
		/// </summary>
		public bool GenerateEmptyCandles { get; set; }

		private Unit _timeout = 10.Percents();

		/// <summary>
		/// The time shift from the time frame end after which a signal is sent to close the unclosed candle forcibly. The default is 10% of the time frame.
		/// </summary>
		public Unit Timeout
		{
			get => _timeout;
			set
			{
				if (value == null)
					throw new ArgumentNullException(nameof(value));

				if (value < 0)
					throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.OffsetValueIncorrect);

				_timeout = value;
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="TimeFrameCandleBuilder"/>.
		/// </summary>
		/// <param name="exchangeInfoProvider">The exchange boards provider.</param>
		public TimeFrameCandleBuilder(IExchangeInfoProvider exchangeInfoProvider)
			: base(exchangeInfoProvider)
		{
			GenerateEmptyCandles = true;
		}

		///// <summary>
		///// Reset state.
		///// </summary>
		//public override void Reset()
		//{
		//	base.Reset();

		//	_timeoutInfos.Clear();
		//}

		/// <inheritdoc />
		protected override TimeFrameCandleMessage CreateCandle(MarketDataMessage message, TimeFrameCandleMessage currentCandle, ICandleBuilderValueTransform transform)
		{
			var timeFrame = message.GetTimeFrame();

			var board = ExchangeInfoProvider.GetOrCreateBoard(message.SecurityId.BoardCode);
			var bounds = timeFrame.GetCandleBounds(transform.Time, board, board.WorkingTime);

			if (transform.Time < bounds.Min)
				return null;

			var openTime = bounds.Min;

			var candle = FirstInitCandle(message, new TimeFrameCandleMessage
			{
				TimeFrame = timeFrame,
				OpenTime = openTime,
				HighTime = openTime,
				LowTime = openTime,
				CloseTime = openTime, // реальное окончание свечи определяет по последней сделке
			}, transform);

			return candle;
		}

		/// <inheritdoc />
		protected override bool IsCandleFinishedBeforeChange(MarketDataMessage message, TimeFrameCandleMessage candle, ICandleBuilderValueTransform transform)
		{
			return transform.Time < candle.OpenTime || (candle.OpenTime + candle.TimeFrame) <= transform.Time;
		}
	}

	/// <summary>
	/// The builder of candles of <see cref="TickCandleMessage"/> type.
	/// </summary>
	public class TickCandleBuilder : CandleBuilder<TickCandleMessage>
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="TickCandleBuilder"/>.
		/// </summary>
		/// <param name="exchangeInfoProvider">The exchange boards provider.</param>
		public TickCandleBuilder(IExchangeInfoProvider exchangeInfoProvider)
			: base(exchangeInfoProvider)
		{
		}

		/// <inheritdoc />
		protected override TickCandleMessage CreateCandle(MarketDataMessage message, TickCandleMessage currentCandle, ICandleBuilderValueTransform transform)
		{
			var time = transform.Time;

			return FirstInitCandle(message, new TickCandleMessage
			{
				MaxTradeCount = message.GetArg<int>(),
				OpenTime = time,
				CloseTime = time,
				HighTime = time,
				LowTime = time,
			}, transform);
		}

		/// <inheritdoc />
		protected override bool IsCandleFinishedBeforeChange(MarketDataMessage message, TickCandleMessage candle, ICandleBuilderValueTransform transform)
		{
			return candle.TotalTicks != null && candle.TotalTicks.Value >= candle.MaxTradeCount;
		}
	}

	/// <summary>
	/// The builder of candles of <see cref="VolumeCandleMessage"/> type.
	/// </summary>
	public class VolumeCandleBuilder : CandleBuilder<VolumeCandleMessage>
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="VolumeCandleBuilder"/>.
		/// </summary>
		/// <param name="exchangeInfoProvider">The exchange boards provider.</param>
		public VolumeCandleBuilder(IExchangeInfoProvider exchangeInfoProvider)
			: base(exchangeInfoProvider)
		{
		}

		/// <inheritdoc />
		protected override VolumeCandleMessage CreateCandle(MarketDataMessage message, VolumeCandleMessage currentCandle, ICandleBuilderValueTransform transform)
		{
			var time = transform.Time;

			return FirstInitCandle(message, new VolumeCandleMessage
			{
				Volume = message.GetArg<decimal>(),
				OpenTime = time,
				CloseTime = time,
				HighTime = time,
				LowTime = time,
			}, transform);
		}

		/// <inheritdoc />
		protected override bool IsCandleFinishedBeforeChange(MarketDataMessage message, VolumeCandleMessage candle, ICandleBuilderValueTransform transform)
		{
			return candle.TotalVolume >= candle.Volume;
		}
	}

	/// <summary>
	/// The builder of candles of <see cref="RangeCandleMessage"/> type.
	/// </summary>
	public class RangeCandleBuilder : CandleBuilder<RangeCandleMessage>
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="RangeCandleBuilder"/>.
		/// </summary>
		/// <param name="exchangeInfoProvider">The exchange boards provider.</param>
		public RangeCandleBuilder(IExchangeInfoProvider exchangeInfoProvider)
			: base(exchangeInfoProvider)
		{
		}

		/// <inheritdoc />
		protected override RangeCandleMessage CreateCandle(MarketDataMessage message, RangeCandleMessage currentCandle, ICandleBuilderValueTransform transform)
		{
			var time = transform.Time;

			return FirstInitCandle(message, new RangeCandleMessage
			{
				PriceRange = message.GetArg<Unit>(),
				OpenTime = time,
				CloseTime = time,
				HighTime = time,
				LowTime = time,
			}, transform);
		}

		/// <inheritdoc />
		protected override bool IsCandleFinishedBeforeChange(MarketDataMessage message, RangeCandleMessage candle, ICandleBuilderValueTransform transform)
		{
			return (decimal)(candle.LowPrice + candle.PriceRange) <= candle.HighPrice;
		}
	}

	/// <summary>
	/// The builder of candles of <see cref="PnFCandleMessage"/> type.
	/// </summary>
	public class PnFCandleBuilder : CandleBuilder<PnFCandleMessage>
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="PnFCandleBuilder"/>.
		/// </summary>
		/// <param name="exchangeInfoProvider">The exchange boards provider.</param>
		public PnFCandleBuilder(IExchangeInfoProvider exchangeInfoProvider)
			: base(exchangeInfoProvider)
		{
		}

		/// <inheritdoc />
		public override void Process(MarketDataMessage message, CandleMessage currentCandle, ICandleBuilderValueTransform transform, ref VolumeProfileBuilder volumeProfile, IList<CandleMessage> changes)
		{
			var currentPnFCandle = (PnFCandleMessage)currentCandle;

			var price = transform.Price;
			var volume = transform.Volume;
			var time = transform.Time;
			var side = transform.Side;
			var oi = transform.OpenInterest;
			var buildFrom = transform.BuildFrom;

			var pnf = message.GetArg<PnFArg>();
			var pnfStep = (decimal)(1 * pnf.BoxSize);

			if (currentPnFCandle == null)
			{
				var openPrice = price.Floor(pnfStep);
				var highPrice = openPrice + pnfStep;

				changes.Add(CreateCandle(message, buildFrom, pnf, openPrice, highPrice, openPrice, highPrice, price, volume, side, time, oi, ref volumeProfile));
			}
			else
			{
				if (currentPnFCandle.LowPrice <= price && price <= currentPnFCandle.HighPrice)
				{
					UpdateCandle(currentPnFCandle, price, volume, time, side, oi, volumeProfile);
					changes.Add(currentPnFCandle);
				}
				else
				{
					var isX = currentPnFCandle.OpenPrice < currentPnFCandle.ClosePrice;

					if (isX)
					{
						if (price > currentPnFCandle.HighPrice)
						{
							currentPnFCandle.HighPrice = currentPnFCandle.ClosePrice = price.Floor(pnfStep) + pnfStep;
							UpdateCandle(currentPnFCandle, price, volume, time, side, oi, volumeProfile);
							changes.Add(currentPnFCandle);
						}
						else if (price < (currentPnFCandle.HighPrice - pnfStep * pnf.ReversalAmount))
						{
							currentPnFCandle.State = CandleStates.Finished;
							changes.Add(currentPnFCandle);

							var highPrice = currentPnFCandle.HighPrice - pnfStep;
							var lowPrice = price.Floor(pnfStep);

							currentPnFCandle = CreateCandle(message, buildFrom, pnf, highPrice, highPrice, lowPrice, lowPrice, price, volume, side, time, oi, ref volumeProfile);
							changes.Add(currentPnFCandle);
						}
						else
						{
							UpdateCandle(currentPnFCandle, price, volume, time, side, oi, volumeProfile);
							changes.Add(currentPnFCandle);
						}
					}
					else
					{
						if (price < currentPnFCandle.LowPrice)
						{
							currentPnFCandle.LowPrice = currentPnFCandle.ClosePrice = price.Floor(pnfStep);
							UpdateCandle(currentPnFCandle, price, volume, time, side, oi, volumeProfile);
							changes.Add(currentPnFCandle);
						}
						else if (price > (currentPnFCandle.LowPrice + pnfStep * pnf.ReversalAmount))
						{
							currentPnFCandle.State = CandleStates.Finished;
							changes.Add(currentPnFCandle);

							var highPrice = price.Floor(pnfStep) + pnfStep;
							var lowPrice = currentPnFCandle.LowPrice + pnfStep;

							currentPnFCandle = CreateCandle(message, buildFrom, pnf, lowPrice, highPrice, lowPrice, highPrice, price, volume, side, time, oi, ref volumeProfile);
							changes.Add(currentPnFCandle);
						}
						else
						{
							UpdateCandle(currentPnFCandle, price, volume, time, side, oi, volumeProfile);
							changes.Add(currentPnFCandle);
						}
					}
				}
			}
		}

		private static void UpdateCandle(PnFCandleMessage currentPnFCandle, decimal price, decimal? volume, DateTimeOffset time, Sides? side, decimal? oi, VolumeProfileBuilder volumeProfile)
		{
			currentPnFCandle.TotalTicks = currentPnFCandle.TotalTicks ?? 0 + 1;

			if (volume != null)
			{
				var v = volume.Value;

				currentPnFCandle.TotalVolume += v;
				currentPnFCandle.TotalPrice += v * price;

				currentPnFCandle.RelativeVolume = currentPnFCandle.RelativeVolume ?? 0 + (side == Sides.Buy ? v : -v);
			}

			currentPnFCandle.CloseVolume = volume;
			currentPnFCandle.CloseTime = time;

			volumeProfile?.Update(price, volume, side);

			currentPnFCandle.OpenInterest = oi;
		}

		private static PnFCandleMessage CreateCandle(MarketDataMessage message, DataType buildFrom, PnFArg pnfArg, decimal openPrice, decimal highPrice, decimal lowPrice, decimal closePrice, decimal price, decimal? volume, Sides? side, DateTimeOffset time, decimal? oi, ref VolumeProfileBuilder volumeProfile)
		{
			var candle = new PnFCandleMessage
			{
				SecurityId = message.SecurityId,
				PnFArg = pnfArg,
				BuildFrom = buildFrom,

				OpenPrice = openPrice,
				ClosePrice = closePrice,
				HighPrice = highPrice,
				LowPrice = lowPrice,
				OpenVolume = volume,
				//CloseVolume = volume,
				HighVolume = volume,
				LowVolume = volume,
				OpenTime = time,
				//CloseTime = time,
				HighTime = time,
				LowTime = time,
				State = CandleStates.Active,
			};

			if (message.IsCalcVolumeProfile)
			{
				var levels = new List<CandlePriceLevel>();
				volumeProfile = new VolumeProfileBuilder(levels);
				candle.PriceLevels = levels;
			}

			UpdateCandle(candle, price, volume, time, side, oi, volumeProfile);

			return candle;
		}
	}

	/// <summary>
	/// The builder of candles of <see cref="RenkoCandleMessage"/> type.
	/// </summary>
	public class RenkoCandleBuilder : CandleBuilder<RenkoCandleMessage>
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="RenkoCandleBuilder"/>.
		/// </summary>
		/// <param name="exchangeInfoProvider">The exchange boards provider.</param>
		public RenkoCandleBuilder(IExchangeInfoProvider exchangeInfoProvider)
			: base(exchangeInfoProvider)
		{
		}

		/// <inheritdoc />
		public override void Process(MarketDataMessage message, CandleMessage currentCandle, ICandleBuilderValueTransform transform, ref VolumeProfileBuilder volumeProfile, IList<CandleMessage> changes)
		{
			var currentRenkoCandle = (RenkoCandleMessage)currentCandle;

			var price = transform.Price;
			var volume = transform.Volume;
			var time = transform.Time;
			var side = transform.Side;
			var oi = transform.OpenInterest;
			var buildFrom = transform.BuildFrom;

			var boxSize = message.GetArg<Unit>();
			var renkoStep = (decimal)(1 * boxSize);

			if (currentRenkoCandle == null)
			{
				var openPrice = price.Floor(renkoStep);

				changes.Add(CreateCandle(message, buildFrom, ref volumeProfile, boxSize, openPrice, renkoStep, price, volume, side, time, oi));
			}
			else
			{
				if (currentRenkoCandle.LowPrice <= price && price <= currentRenkoCandle.HighPrice)
				{
					currentRenkoCandle.TotalTicks++;

					if (volume != null)
					{
						currentRenkoCandle.TotalVolume += volume.Value;
						currentRenkoCandle.TotalPrice += volume.Value * price;

						currentRenkoCandle.RelativeVolume += side == Sides.Buy ? volume : -volume;
					}

					currentRenkoCandle.CloseVolume = volume;
					currentRenkoCandle.CloseTime = time;

					volumeProfile?.Update(price, volume, side);

					currentRenkoCandle.OpenInterest = oi;

					changes.Add(currentRenkoCandle);
				}
				else
				{
					currentRenkoCandle.State = CandleStates.Finished;
					changes.Add(currentRenkoCandle);

					int times;
					bool isUp;
					decimal openPrice;

					if (price < currentRenkoCandle.LowPrice)
					{
						times = (int)((currentRenkoCandle.LowPrice - price) / renkoStep) + 1;
						isUp = false;
						openPrice = currentRenkoCandle.LowPrice;
					}
					else
					{
						times = (int)((price - currentRenkoCandle.HighPrice) / renkoStep) + 1;
						isUp = true;
						openPrice = currentRenkoCandle.HighPrice;
					}

					for (var i = 0; i < times; i++)
					{
						if (isUp)
						{
							currentRenkoCandle = CreateCandle(message, buildFrom, ref volumeProfile, boxSize, openPrice, renkoStep, price, volume, side, time, oi);
							changes.Add(currentRenkoCandle);
							openPrice += renkoStep;
						}
						else
						{
							currentRenkoCandle = CreateCandle(message, buildFrom, ref volumeProfile, boxSize, openPrice, -renkoStep, price, volume, side, time, oi);
							changes.Add(currentRenkoCandle);
							openPrice -= renkoStep;
						}

						currentRenkoCandle.State = CandleStates.Finished;
					}

					currentRenkoCandle.State = CandleStates.Active;
				}
			}
		}

		private static RenkoCandleMessage CreateCandle(MarketDataMessage message, DataType buildFrom, ref VolumeProfileBuilder volumeProfile, Unit boxSize, decimal openPrice, decimal renkoStep, decimal price, decimal? volume, Sides? side, DateTimeOffset time, decimal? oi)
		{
			var candle = new RenkoCandleMessage
			{
				SecurityId = message.SecurityId,
				BoxSize = boxSize,
				BuildFrom = buildFrom,

				OpenPrice = openPrice,
				ClosePrice = openPrice + renkoStep,
				//HighPrice = openPrice + renkoStep,
				//LowPrice = openPrice,
				OpenVolume = volume,
				CloseVolume = volume,
				HighVolume = volume,
				LowVolume = volume,
				OpenTime = time,
				CloseTime = time,
				HighTime = time,
				LowTime = time,
				RelativeVolume = side == null ? null : (side == Sides.Buy ? volume : -volume),
				TotalTicks = 1,
				State = CandleStates.Active,
				OpenInterest = oi,
			};

			if (volume != null)
			{
				candle.TotalPrice += price * volume.Value;
				candle.TotalVolume += volume.Value;
			}

			if (renkoStep > 0)
			{
				candle.HighPrice = candle.ClosePrice;
				candle.LowPrice = candle.OpenPrice;
			}
			else
			{
				candle.HighPrice = candle.OpenPrice;
				candle.LowPrice = candle.ClosePrice;
			}

			if (message.IsCalcVolumeProfile)
			{
				var levels = new List<CandlePriceLevel>();

				volumeProfile = new VolumeProfileBuilder(levels);
				volumeProfile.Update(price, volume, side);

				candle.PriceLevels = levels;
			}

			return candle;
		}
	}

	/// <summary>
	/// The builder of candles of <see cref="HeikinAshiCandleBuilder"/> type.
	/// </summary>
	public class HeikinAshiCandleBuilder : CandleBuilder<HeikinAshiCandleMessage>
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="HeikinAshiCandleBuilder"/>.
		/// </summary>
		/// <param name="exchangeInfoProvider">The exchange boards provider.</param>
		public HeikinAshiCandleBuilder(IExchangeInfoProvider exchangeInfoProvider)
			: base(exchangeInfoProvider)
		{
		}

		/// <inheritdoc />
		protected override HeikinAshiCandleMessage CreateCandle(MarketDataMessage message, HeikinAshiCandleMessage currentCandle, ICandleBuilderValueTransform transform)
		{
			var timeFrame = message.GetTimeFrame();

			var board = ExchangeInfoProvider.GetOrCreateBoard(message.SecurityId.BoardCode);
			var bounds = timeFrame.GetCandleBounds(transform.Time, board, board.WorkingTime);

			if (transform.Time < bounds.Min)
				return null;

			var openTime = bounds.Min;

			var candle = FirstInitCandle(message, new HeikinAshiCandleMessage
			{
				TimeFrame = timeFrame,
				OpenTime = openTime,
				HighTime = openTime,
				LowTime = openTime,
				CloseTime = openTime,
			}, transform);

			if (currentCandle != null)
				candle.OpenPrice = (currentCandle.OpenPrice + currentCandle.ClosePrice) / 2M;

			return candle;
		}

		/// <inheritdoc />
		protected override bool IsCandleFinishedBeforeChange(MarketDataMessage message, HeikinAshiCandleMessage candle, ICandleBuilderValueTransform transform)
		{
			return transform.Time < candle.OpenTime || (candle.OpenTime + candle.TimeFrame) <= transform.Time;
		}

		/// <inheritdoc />
		protected override void UpdateCandle(MarketDataMessage message, HeikinAshiCandleMessage candle, ICandleBuilderValueTransform transform)
		{
			base.UpdateCandle(message, candle, transform);

			candle.ClosePrice = (candle.OpenPrice + candle.HighPrice + candle.LowPrice + candle.ClosePrice) / 4M;
		}
	}
}