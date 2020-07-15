#region S# License
/******************************************************************************************
NOTICE!!!  This program and source code is owned and licensed by
StockSharp, LLC, www.stocksharp.com
Viewing or use of this code requires your acceptance of the license
agreement found at https://github.com/StockSharp/StockSharp/blob/master/LICENSE
Removal of this comment is a violation of the license agreement.

Project: StockSharp.Algo.Storages.Csv.Algo
File: TickCsvSerializer.cs
Created: 2015, 12, 14, 1:43 PM

Copyright 2010 by StockSharp, LLC
*******************************************************************************************/
#endregion S# License
namespace StockSharp.Algo.Storages.Csv
{
	using System;
	using System.Text;

	using Ecng.Common;

	using StockSharp.Messages;

	/// <summary>
	/// The tick serializer in the CSV format.
	/// </summary>
	public class TickCsvSerializer : CsvMarketDataSerializer<ExecutionMessage>
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="TickCsvSerializer"/>.
		/// </summary>
		/// <param name="securityId">Security ID.</param>
		/// <param name="encoding">Encoding.</param>
		public TickCsvSerializer(SecurityId securityId, Encoding encoding = null)
			: base(securityId, encoding)
		{
		}

		/// <inheritdoc />
		public override IMarketDataMetaInfo CreateMetaInfo(DateTime date)
		{
			return new CsvMetaInfo(date, Encoding, r => r.ReadNullableLong());
		}

		/// <inheritdoc />
		protected override void Write(CsvFileWriter writer, ExecutionMessage data, IMarketDataMetaInfo metaInfo)
		{
			writer.WriteRow(new[]
			{
				data.ServerTime.WriteTimeMls(),
				data.ServerTime.ToString("zzz"),
				data.TradeId.ToString(),
				data.TradePrice.ToString(),
				data.TradeVolume.ToString(),
				data.OriginSide.ToString(),
				data.OpenInterest.ToString(),
				data.IsSystem.ToString(),
				data.IsUpTick.ToString(),
				data.TradeStringId,
				data.Currency.ToString(),
			}.Concat(data.BuildFrom.ToCsv()));

			metaInfo.LastTime = data.ServerTime.UtcDateTime;
			metaInfo.LastId = data.TradeId;
		}

		/// <inheritdoc />
		protected override ExecutionMessage Read(FastCsvReader reader, IMarketDataMetaInfo metaInfo)
		{
			var execMsg = new ExecutionMessage
			{
				SecurityId = SecurityId,
				ExecutionType = ExecutionTypes.Tick,
				ServerTime = reader.ReadTime(metaInfo.Date),
				TradeId = reader.ReadNullableLong(),
				TradePrice = reader.ReadNullableDecimal(),
				TradeVolume = reader.ReadNullableDecimal(),
				OriginSide = reader.ReadNullableEnum<Sides>(),
				OpenInterest = reader.ReadNullableDecimal(),
				IsSystem = reader.ReadNullableBool(),
			};

			if ((reader.ColumnCurr + 1) < reader.ColumnCount)
			{
				execMsg.IsUpTick = reader.ReadNullableBool();
				execMsg.TradeStringId = reader.ReadString();
				execMsg.Currency = reader.ReadNullableEnum<CurrencyTypes>();
			}

			if ((reader.ColumnCurr + 1) < reader.ColumnCount)
				execMsg.BuildFrom = reader.ReadBuildFrom();

			return execMsg;
		}
	}
}