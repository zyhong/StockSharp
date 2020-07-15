﻿namespace SampleConnection
{
	using System;
	using System.ComponentModel;
	using System.Windows.Media;

	using StockSharp.Algo;
	using StockSharp.Algo.Candles;
	using StockSharp.Xaml.Charting;

	partial class ChartWindow
	{
		private readonly Connector _connector;
		private readonly CandleSeries _candleSeries;
		private readonly ChartCandleElement _candleElem;
		private readonly Subscription _subscription;

		public ChartWindow(CandleSeries candleSeries)
		{
			if (candleSeries == null)
				throw new ArgumentNullException(nameof(candleSeries));

			InitializeComponent();

			Title = candleSeries.ToString();

			_candleSeries = candleSeries;
			_connector = MainWindow.Instance.MainPanel.Connector;

			Chart.ChartTheme = ChartThemes.ExpressionDark;

			var area = new ChartArea();
			Chart.Areas.Add(area);

			_candleElem = new ChartCandleElement
			{
				AntiAliasing = false,
				UpFillColor = Colors.White,
				UpBorderColor = Colors.Black,
				DownFillColor = Colors.Black,
				DownBorderColor = Colors.Black,
			};

			area.Elements.Add(_candleElem);

			_connector.CandleSeriesProcessing += ProcessNewCandle;
			_subscription = _connector.SubscribeCandles(_candleSeries);
		}

		public bool SeriesInactive { get; set; }

		private void ProcessNewCandle(CandleSeries series, Candle candle)
		{
			if (series != _candleSeries)
				return;

			Chart.Draw(_candleElem, candle);
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			_connector.CandleSeriesProcessing -= ProcessNewCandle;

			if (!SeriesInactive && _subscription.State.IsActive())
				_connector.UnSubscribe(_subscription);

			base.OnClosing(e);
		}
	}
}
