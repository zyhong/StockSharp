#region S# License
/******************************************************************************************
NOTICE!!!  This program and source code is owned and licensed by
StockSharp, LLC, www.stocksharp.com
Viewing or use of this code requires your acceptance of the license
agreement found at https://github.com/StockSharp/StockSharp/blob/master/LICENSE
Removal of this comment is a violation of the license agreement.

Project: StockSharp.Algo.Positions.Algo
File: IPositionManager.cs
Created: 2015, 11, 11, 2:32 PM

Copyright 2010 by StockSharp, LLC
*******************************************************************************************/
#endregion S# License
namespace StockSharp.Algo.Positions
{
	using StockSharp.Messages;

	/// <summary>
	/// The interface for the position calculation manager.
	/// </summary>
	public interface IPositionManager
	{
		/// <summary>
		/// To calculate position.
		/// </summary>
		/// <param name="message">Message.</param>
		/// <returns>The position by order or trade.</returns>
		PositionChangeMessage ProcessMessage(Message message);
	}
}