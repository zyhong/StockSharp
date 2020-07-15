﻿namespace StockSharp.Algo
{
	using System;
	using System.Collections.Generic;
	using System.Linq;

	using Ecng.Collections;
	using Ecng.Common;

	using StockSharp.Algo.Storages;
	using StockSharp.Localization;
	using StockSharp.Logging;
	using StockSharp.Messages;

	/// <summary>
	/// Security native id message adapter.
	/// </summary>
	public class SecurityNativeIdMessageAdapter : MessageAdapterWrapper
	{
		private sealed class ProcessSuspendedSecurityMessage : Message
		{
			public SecurityId SecurityId { get; }

			public ProcessSuspendedSecurityMessage(IMessageAdapter adapter, SecurityId securityId)
				: base(ExtendedMessageTypes.ProcessSuspendedSecurityMessages)
			{
				this.LoopBack(adapter);
				SecurityId = securityId;
			}

			public override Message Clone()
			{
				return new ProcessSuspendedSecurityMessage(Adapter, SecurityId);
			}
		}

		private readonly PairSet<object, SecurityId> _securityIds = new PairSet<object, SecurityId>();
		private readonly Dictionary<SecurityId, List<Message>> _suspendedInMessages = new Dictionary<SecurityId, List<Message>>();
		private readonly Dictionary<SecurityId, RefPair<List<Message>, Dictionary<MessageTypes, Message>>> _suspendedOutMessages = new Dictionary<SecurityId, RefPair<List<Message>, Dictionary<MessageTypes, Message>>>();
		private readonly HashSet<long> _skipTransactions = new HashSet<long>();
		private readonly SyncObject _syncRoot = new SyncObject();

		/// <summary>
		/// Security native identifier storage.
		/// </summary>
		public INativeIdStorage Storage { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="SecurityNativeIdMessageAdapter"/>.
		/// </summary>
		/// <param name="innerAdapter">The adapter, to which messages will be directed.</param>
		/// <param name="storage">Security native identifier storage.</param>
		public SecurityNativeIdMessageAdapter(IMessageAdapter innerAdapter, INativeIdStorage storage)
			: base(innerAdapter)
		{
			Storage = storage ?? throw new ArgumentNullException(nameof(storage));
			Storage.Added += OnStorageNewIdentifierAdded;
		}

		/// <inheritdoc />
		public override void Dispose()
		{
			Storage.Added -= OnStorageNewIdentifierAdded;
			base.Dispose();
		}

		/// <inheritdoc />
		protected override void OnInnerAdapterNewOutMessage(Message message)
		{
			switch (message.Type)
			{
				case MessageTypes.Connect:
				{
					var nativeIds = Storage.Get(StorageName);

					lock (_syncRoot)
					{
						foreach (var tuple in nativeIds)
						{
							var securityId = tuple.Item1;
							var nativeId = tuple.Item2;

							_securityIds[nativeId] = securityId;
						}
					}

					base.OnInnerAdapterNewOutMessage(message);
					break;
				}

				case MessageTypes.Security:
				{
					var secMsg = (SecurityMessage)message;
					var securityId = secMsg.SecurityId;

					var nativeSecurityId = securityId.Native;
					var securityCode = securityId.SecurityCode;
					var boardCode = securityId.BoardCode;

					if (securityCode.IsEmpty())
						throw new InvalidOperationException();

					var noNative = securityId;
					noNative.Native = null;
					
					if (!boardCode.IsEmpty())
					{
						if (nativeSecurityId != null)
						{
							var storageName = StorageName;

							if (!Storage.TryAdd(storageName, noNative, nativeSecurityId, IsNativeIdentifiersPersistable))
							{
								var prevId = Storage.TryGetByNativeId(storageName, nativeSecurityId);

								if (prevId != null)
								{
									if (noNative != prevId.Value)
									{
										//throw new InvalidOperationException(LocalizedStrings.Str687Params.Put(securityId, prevId.Value, nativeSecurityId));
										this.AddWarningLog(LocalizedStrings.Str687Params.Put(noNative, prevId.Value, nativeSecurityId));
										
										Storage.RemoveBySecurityId(storageName, prevId.Value);
										Storage.TryAdd(storageName, noNative, nativeSecurityId, IsNativeIdentifiersPersistable);
									}
								}
								else
								{
									//throw new InvalidOperationException(LocalizedStrings.Str687Params.Put(Storage.TryGetBySecurityId(storageName, securityId), nativeSecurityId, securityId));
									this.AddWarningLog(LocalizedStrings.Str687Params.Put(Storage.TryGetBySecurityId(storageName, noNative), nativeSecurityId, noNative));
									
									Storage.RemoveByNativeId(storageName, nativeSecurityId);
									Storage.TryAdd(storageName, noNative, nativeSecurityId, IsNativeIdentifiersPersistable);
								}
							}

							lock (_syncRoot)
								_securityIds[nativeSecurityId] = noNative;
						}
					}
					else
					{
						// TODO
					}

					// external code shouldn't receive native ids
					secMsg.SecurityId = noNative;

					base.OnInnerAdapterNewOutMessage(message);

					ProcessSuspendedSecurityMessages(securityId);

					break;
				}

				case MessageTypes.PositionChange:
				{
					var positionMsg = (PositionChangeMessage)message;

					ProcessMessage(positionMsg, true, (prev, curr) =>
					{
						foreach (var pair in prev.Changes)
						{
							curr.Changes.TryAdd(pair.Key, pair.Value);
						}

						return curr;
					});
					break;
				}

				case MessageTypes.Execution:
				{
					var execMsg = (ExecutionMessage)message;
					ProcessMessage(execMsg, execMsg.ExecutionType == ExecutionTypes.Tick && execMsg.OriginalTransactionId == 0, null);
					break;
				}

				case MessageTypes.Level1Change:
				{
					var level1Msg = (Level1ChangeMessage)message;

					ProcessMessage(level1Msg, true, (prev, curr) =>
					{
						foreach (var pair in prev.Changes)
						{
							curr.Changes.TryAdd(pair.Key, pair.Value);
						}

						return curr;
					});
					break;
				}

				case MessageTypes.QuoteChange:
				{
					var quotesMsg = (QuoteChangeMessage)message;

					ProcessMessage(quotesMsg, true, (prev, curr) => curr);
					break;
				}

				case MessageTypes.News:
				{
					var newsMsg = (NewsMessage)message;

					if (newsMsg.SecurityId != null)
						ProcessMessage(newsMsg.SecurityId.Value, newsMsg, true, null);
					else
						base.OnInnerAdapterNewOutMessage(message);

					break;
				}

				default:
				{
					if (message is CandleMessage candleMsg)
						ProcessMessage(candleMsg, candleMsg.OriginalTransactionId == 0, null);
					else
						base.OnInnerAdapterNewOutMessage(message);

					break;
				}
			}
		}

		/// <inheritdoc />
		protected override bool OnSendInMessage(Message message)
		{
			switch (message.Type)
			{
				case MessageTypes.Reset:
				{
					lock (_syncRoot)
					{
						_securityIds.Clear();
						_suspendedOutMessages.Clear();
						_suspendedInMessages.Clear();
						_skipTransactions.Clear();
					}

					break;
				}
				case MessageTypes.OrderRegister:
				case MessageTypes.OrderReplace:
				case MessageTypes.OrderCancel:
				case MessageTypes.OrderGroupCancel:
				case MessageTypes.MarketData:
				{
					var secMsg = (SecurityMessage)message;

					if (secMsg.SecurityId == default)
						break;

					var securityId = secMsg.SecurityId;

					var native = GetNativeId(secMsg, securityId);

					if (native == null)
					{
						if (securityId.Native != null)
						{
							lock (_syncRoot)
								_skipTransactions.Add(((ITransactionIdMessage)secMsg).TransactionId);
							
							break;
						}

						return true;
					}

					securityId.Native = native;
					message.ReplaceSecurityId(securityId);

					break;
				}

				case MessageTypes.OrderPairReplace:
				{
					var pairMsg = (OrderPairReplaceMessage)message;

					var securityId1 = pairMsg.Message1.SecurityId;
					var securityId2 = pairMsg.Message2.SecurityId;

					if (securityId1.Native != null && securityId2.Native != null)
						break;

					var nativeId1 = GetNativeId(pairMsg, securityId1);

					if (nativeId1 == null)
						return true;

					var nativeId2 = GetNativeId(pairMsg, securityId2);

					if (nativeId2 == null)
						return true;

					securityId1.Native = nativeId1;
					pairMsg.Message1.ReplaceSecurityId(securityId1);

					securityId2.Native = nativeId2;
					pairMsg.Message2.ReplaceSecurityId(securityId2);
					break;
				}

				case ExtendedMessageTypes.ProcessSuspendedSecurityMessages:
					ProcessSuspendedSecurityMessages(((ProcessSuspendedSecurityMessage)message).SecurityId);
					break;
			}

			return base.OnSendInMessage(message);
		}

		private object GetNativeId(Message message, SecurityId securityId)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));

			lock (_syncRoot)
			{
				var native = _securityIds.TryGetKey(securityId);

				if (native != null)
					return native;

				var clone = message.Clone();
				clone.LoopBack(this);
				_suspendedInMessages.SafeAdd(securityId).Add(clone);
			}

			this.AddInfoLog("Suspended {0}.", message);
			return null;
		}

		/// <summary>
		/// Create a copy of <see cref="SecurityNativeIdMessageAdapter"/>.
		/// </summary>
		/// <returns>Copy.</returns>
		public override IMessageChannel Clone()
		{
			return new SecurityNativeIdMessageAdapter(InnerAdapter.TypedClone(), Storage);
		}

		private void ProcessMessage<TMessage>(TMessage message, bool throwIfSecIdEmpty, Func<TMessage, TMessage, TMessage> processSuspend)
			where TMessage : Message, IOriginalTransactionIdMessage, ISecurityIdMessage
		{
			ProcessMessage(message.SecurityId, message, throwIfSecIdEmpty, processSuspend);
		}

		private void ProcessMessage<TMessage>(SecurityId securityId, TMessage message, bool throwIfSecIdEmpty, Func<TMessage, TMessage, TMessage> processSuspend)
			where TMessage : Message, IOriginalTransactionIdMessage
		{
			bool skip;

			lock (_syncRoot)
				skip = _skipTransactions.Contains(message.OriginalTransactionId);

			if (skip)
			{
				base.OnInnerAdapterNewOutMessage(message);
				return;
			}

			var native = securityId.Native;

			if (native != null)
			{
				SecurityId? fullSecurityId;

				lock (_syncRoot)
					fullSecurityId = _securityIds.TryGetValue2(native);

				if (fullSecurityId == null)
				{
					lock (_syncRoot)
					{
						var tuple = _suspendedOutMessages.SafeAdd(securityId, key => RefTuple.Create((List<Message>)null, (Dictionary<MessageTypes, Message>)null));

						var clone = message.Clone();

						if (processSuspend == null)
						{
							if (tuple.First == null)
								tuple.First = new List<Message>();

							tuple.First.Add(clone);
						}
						else
						{
							if (tuple.Second == null)
								tuple.Second = new Dictionary<MessageTypes, Message>();

							var prev = tuple.Second.TryGetValue(clone.Type);

							tuple.Second[clone.Type] = prev == null
								? clone
								: processSuspend((TMessage)prev, (TMessage)clone);
						}
					}

					return;
				}

				message.ReplaceSecurityId(fullSecurityId.Value);
			}
			else
			{
				var securityCode = securityId.SecurityCode;
				var boardCode = securityId.BoardCode;

				var isSecCodeEmpty = securityCode.IsEmpty();

				if (isSecCodeEmpty && throwIfSecIdEmpty)
					throw new InvalidOperationException();

				if (!isSecCodeEmpty && boardCode.IsEmpty())
				{
					SecurityId? foundId = null;

					lock (_syncRoot)
					{
						foreach (var id in _securityIds.Values)
						{
							if (!id.SecurityCode.CompareIgnoreCase(securityCode))
								continue;

							//if (securityId.SecurityType != null && securityId.SecurityType != id.SecurityType)
							//	continue;

							foundId = id;
						}

						if (foundId == null)
						{
							var tuple = _suspendedOutMessages.SafeAdd(securityId, key => RefTuple.Create(new List<Message>(), (Dictionary<MessageTypes, Message>)null));
							tuple.First.Add(message.Clone());
							return;
						}
					}

					message.ReplaceSecurityId(foundId.Value);

					//// если указан код и тип инструмента, то пытаемся найти инструмент по ним
					//if (securityId.SecurityType != null)
					//{

					//}
					//else
					//	throw new ArgumentException(nameof(securityId), LocalizedStrings.Str682Params.Put(securityCode, securityId.SecurityType));
				}
			}

			base.OnInnerAdapterNewOutMessage(message);
		}

		private void ProcessSuspendedSecurityMessages(SecurityId securityId)
		{
			var noNativeId = securityId.Native == null ? (SecurityId?)null : securityId;

			if (noNativeId != null)
			{
				var t = noNativeId.Value;
				t.Native = null;
				noNativeId = t;
			}

			List<Message> msgs = null;

			lock (_syncRoot)
			{
				var tuple = _suspendedOutMessages.TryGetValue(securityId);

				if (tuple != null)
				{
					msgs = GetMessages(tuple);
					_suspendedOutMessages.Remove(securityId);
				}

				if (noNativeId != null)
				{
					tuple = _suspendedOutMessages.TryGetValue(noNativeId.Value);

					if (tuple != null)
					{
						if (msgs == null)
							msgs = GetMessages(tuple);
						else
							msgs.AddRange(GetMessages(tuple));

						_suspendedOutMessages.Remove(noNativeId.Value);
					}
				}

				// find association by code and code + type
				var pair = _suspendedOutMessages
					.FirstOrDefault(p =>
						p.Key.SecurityCode.CompareIgnoreCase(securityId.SecurityCode) &&
						p.Key.BoardCode.IsEmpty() /*&&
						(securityId.SecurityType == null || p.Key.SecurityType == securityId.SecurityType)*/);

				var value = pair.Value;

				if (value != null)
				{
					_suspendedOutMessages.Remove(pair.Key);

					if (msgs == null)
						msgs = GetMessages(value);
					else
						msgs.AddRange(GetMessages(value));
				}

				var inMsgs = _suspendedInMessages.TryGetValue(securityId);

				if (inMsgs != null)
				{
					if (msgs == null)
						msgs = inMsgs;
					else
						msgs.AddRange(inMsgs);

					_suspendedInMessages.Remove(securityId);
				}

				if (noNativeId != null)
				{
					inMsgs = _suspendedInMessages.TryGetValue(noNativeId.Value);

					if (inMsgs != null)
					{
						if (msgs == null)
							msgs = inMsgs;
						else
							msgs.AddRange(inMsgs);

						_suspendedInMessages.Remove(noNativeId.Value);
					}
				}
			}

			if (msgs == null)
				return;

			// external code shouldn't receive native ids
			securityId.Native = null;

			foreach (var msg in msgs)
			{
				msg.ReplaceSecurityId(securityId);
				base.OnInnerAdapterNewOutMessage(msg);
			}
		}

		private void OnStorageNewIdentifierAdded(string storageName, SecurityId securityId, object nativeId)
		{
			if (!StorageName.CompareIgnoreCase(storageName))
				return;

			bool added;

			lock (_syncRoot)
				added = _securityIds.TryAdd(nativeId, securityId);

			if (added)
			{
				var temp = securityId;
				temp.Native = nativeId;
				RaiseNewOutMessage(new ProcessSuspendedSecurityMessage(this, temp));
			}
		}

		private static List<Message> GetMessages(RefPair<List<Message>, Dictionary<MessageTypes, Message>> tuple)
		{
			var retVal = tuple.First;

			if (retVal == null)
				retVal = tuple.Second.Values.ToList();
			else if (tuple.Second != null)
				retVal.AddRange(tuple.Second.Values);

			return retVal;
		}
	}
}