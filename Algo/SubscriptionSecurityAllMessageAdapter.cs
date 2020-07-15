﻿namespace StockSharp.Algo
{
	using System;
	using System.Collections.Generic;
	using System.Linq;

	using Ecng.Collections;
	using Ecng.Common;

	using StockSharp.Logging;
	using StockSharp.Messages;
	using StockSharp.Localization;

	/// <summary>
	/// Security ALL subscription counter adapter.
	/// </summary>
	public class SubscriptionSecurityAllMessageAdapter : MessageAdapterWrapper
	{
		private abstract class BaseSubscription
		{
			protected BaseSubscription(MarketDataMessage origin)
			{
				Origin = origin ?? throw new ArgumentNullException(nameof(origin));
			}

			public MarketDataMessage Origin { get; }
		}

		private class ChildSubscription : BaseSubscription
		{
			public ChildSubscription(ParentSubscription parent, MarketDataMessage origin)
				: base(origin)
			{
				Parent = parent ?? throw new ArgumentNullException(nameof(parent));
			}

			public ParentSubscription Parent { get; }
			public SubscriptionStates State { get; set; } = SubscriptionStates.Stopped;
			public List<ISubscriptionIdMessage> Suspended { get; } = new List<ISubscriptionIdMessage>();
			public CachedSynchronizedDictionary<long, MarketDataMessage> Subscribers { get; } = new CachedSynchronizedDictionary<long, MarketDataMessage>();
		}

		private class ParentSubscription : BaseSubscription
		{
			public ParentSubscription(MarketDataMessage origin)
				: base(origin)
			{
			}

			public CachedSynchronizedPairSet<long, MarketDataMessage> Alls = new CachedSynchronizedPairSet<long, MarketDataMessage>();
			public Dictionary<SecurityId, ChildSubscription> Child { get; } = new Dictionary<SecurityId, ChildSubscription>();
		}

		private readonly SyncObject _sync = new SyncObject();

		private readonly Dictionary<long, RefPair<long, SubscriptionStates>> _pendingLoopbacks = new Dictionary<long, RefPair<long, SubscriptionStates>>();
		private readonly Dictionary<long, ParentSubscription> _parents = new Dictionary<long, ParentSubscription>();
		private readonly Dictionary<long, ParentSubscription> _unsubscribes = new Dictionary<long, ParentSubscription>();
		private readonly Dictionary<long, Tuple<ParentSubscription, MarketDataMessage>> _requests = new Dictionary<long, Tuple<ParentSubscription, MarketDataMessage>>();
		private readonly List<ChildSubscription> _toFlush = new List<ChildSubscription>();

		/// <summary>
		/// Initializes a new instance of the <see cref="SubscriptionSecurityAllMessageAdapter"/>.
		/// </summary>
		/// <param name="innerAdapter">Inner message adapter.</param>
		public SubscriptionSecurityAllMessageAdapter(IMessageAdapter innerAdapter)
			: base(innerAdapter)
		{
		}

		private void ClearState()
		{
			lock (_sync)
			{
				_pendingLoopbacks.Clear();
				_parents.Clear();
				_unsubscribes.Clear();
				_requests.Clear();
				_toFlush.Clear();
			}
		}

		/// <inheritdoc />
		protected override bool OnSendInMessage(Message message)
		{
			switch (message.Type)
			{
				case MessageTypes.Reset:
					ClearState();
					break;

				case MessageTypes.MarketData:
				{
					var mdMsg = (MarketDataMessage)message;

					if (mdMsg.IsSubscribe)
					{
						var transId = mdMsg.TransactionId;

						lock (_sync)
						{
							if (_pendingLoopbacks.TryGetAndRemove(transId, out var tuple))
							{
								if (tuple.Second != SubscriptionStates.Stopped)
								{
									if (tuple.Second == SubscriptionStates.Finished)
									{
										RaiseNewOutMessage(new SubscriptionFinishedMessage
										{
											OriginalTransactionId = transId,
										});
									}
									else
									{
										RaiseNewOutMessage(new SubscriptionResponseMessage
										{
											OriginalTransactionId = transId,
											Error = new InvalidOperationException(LocalizedStrings.SubscriptionInvalidState.Put(transId, tuple.Second)),
										});
									}

									return true;
								}

								var child = _parents[tuple.First].Child[mdMsg.SecurityId];
								child.State = SubscriptionStates.Online;

								if (child.Suspended.Count > 0)
									_toFlush.Add(child);

								this.AddDebugLog("New ALL map (active): {0}/{1} TrId={2}", child.Origin.SecurityId, child.Origin.DataType2, mdMsg.TransactionId);
								
								// for child subscriptions make online (or finished) immediatelly
								RaiseNewOutMessage(mdMsg.CreateResponse());
								//RaiseNewOutMessage(mdMsg.CreateResult());
								return true;
							}
							else
							{
								if (!IsSecurityRequired(mdMsg.DataType2) || mdMsg.SecurityId == default)
								{
									var parent = _parents.FirstOrDefault(p => p.Value.Origin.DataType2 == mdMsg.DataType2).Value;

									if (parent == null)
									{
										parent = new ParentSubscription(mdMsg.TypedClone());
										_parents.Add(transId, parent);

										if (mdMsg.SecurityId == default)
										{
											// first ALL is initiator
											parent.Alls.Add(transId, parent.Origin);
										}

										_requests.Add(transId, Tuple.Create(parent, parent.Origin));

										// do not specify security cause adapter doesn't require it
										Extensions.AllSecurity.CopyEx(mdMsg, false);

										this.AddInfoLog("Sec ALL {0} subscribing.", transId);
									}
									else
									{
										var childs = parent.Child;

										mdMsg = mdMsg.TypedClone();

										if (mdMsg.SecurityId != default)
										{
											var child = childs.SafeAdd(mdMsg.SecurityId, key => new ChildSubscription(parent, mdMsg));
											child.Subscribers.Add(transId, mdMsg);
										}
										else
										{
											parent.Alls.Add(transId, mdMsg);
										}

										_requests.Add(transId, Tuple.Create(parent, mdMsg));

										// for child subscriptions make online (or finished) immediatelly
										RaiseNewOutMessage(mdMsg.CreateResponse());
										//RaiseNewOutMessage(mdMsg.CreateResult());
										return true;
									}
								}
							}
						}
					}
					else
					{
						var originId = mdMsg.OriginalTransactionId;

						lock (_sync)
						{
							var found = false;

							if (!_requests.TryGetAndRemove(originId, out var tuple))
								break;

							//this.AddDebugLog("Sec ALL child {0} unsubscribe.", originId);

							var parent = tuple.Item1;
							var request = tuple.Item2;

							if (parent.Alls.RemoveByValue(request))
								found = true;
							else
							{
								if (parent.Child.TryGetValue(request.SecurityId, out var child))
								{
									if (child.Subscribers.Remove(request.TransactionId))
									{
										found = true;

										if (child.Subscribers.Count == 0)
											parent.Child.Remove(request.SecurityId);
									}
								}
							}

							if (found)
							{
								if (parent.Alls.Count == 0 && parent.Child.Count == 0)
								{
									// last unsubscribe is not initial subscription
									if (parent.Origin.TransactionId != originId)
									{
										mdMsg = mdMsg.TypedClone();
										mdMsg.OriginalTransactionId = parent.Origin.TransactionId;
											
										message = mdMsg;
									}

									_unsubscribes.Add(mdMsg.TransactionId, parent);
										
									break;
								}
							}

							RaiseNewOutMessage(mdMsg.CreateResponse(found ? null : new InvalidOperationException(LocalizedStrings.SubscriptionNonExist.Put(originId))));
							return true;
						}
					}

					break;
				}
			}

			return base.OnSendInMessage(message);
		}

		/// <inheritdoc />
		protected override void OnInnerAdapterNewOutMessage(Message message)
		{
			List<Message> extra = null;

			switch (message.Type)
			{
				case MessageTypes.Disconnect:
				case ExtendedMessageTypes.ReconnectingFinished:
				{
					ClearState();
					break;
				}
				case MessageTypes.SubscriptionResponse:
				{
					var responseMsg = (SubscriptionResponseMessage)message;
					var originId = responseMsg.OriginalTransactionId;

					if (responseMsg.Error != null)
					{
						lock (_sync)
						{
							if (_parents.TryGetAndRemove(originId, out var parent))
							{
								this.AddErrorLog("Sec ALL {0} error.", parent.Origin.TransactionId);
							
								extra = new List<Message>();

								foreach (var child in parent.Child.Values)
								{
									var childId = child.Origin.TransactionId;

									if (_pendingLoopbacks.TryGetValue(childId, out var tuple) && tuple.Second == SubscriptionStates.Stopped)
									{
										// loopback subscription not yet come, so will reply later
										tuple.Second = SubscriptionStates.Error;
									}
									else
										extra.Add(new SubscriptionResponseMessage { OriginalTransactionId = childId, Error = responseMsg.Error });
								}
							}
							else if (_unsubscribes.TryGetAndRemove(originId, out parent))
							{
								this.AddErrorLog("Sec ALL {0} unsubscribe error.", parent.Origin.TransactionId);
								_parents.Remove(parent.Origin.TransactionId);
							}
						}
					}
					else
					{
						if (_unsubscribes.TryGetAndRemove(originId, out var parent))
						{
							this.AddInfoLog("Sec ALL {0} unsubscribed.", parent.Origin.TransactionId);
							_parents.Remove(parent.Origin.TransactionId);
						}
					}

					break;
				}
				case MessageTypes.SubscriptionFinished:
				{
					var finishMsg = (SubscriptionFinishedMessage)message;

					lock (_sync)
					{
						if (_parents.TryGetAndRemove(finishMsg.OriginalTransactionId, out var parent))
						{
							extra = new List<Message>();

							foreach (var child in parent.Child.Values)
							{
								var childId = child.Origin.TransactionId;

								if (_pendingLoopbacks.TryGetValue(childId, out var tuple) && tuple.Second == SubscriptionStates.Stopped)
								{
									// loopback subscription not yet come, so will reply later
									tuple.Second = SubscriptionStates.Finished;
								}
								else
									extra.Add(new SubscriptionFinishedMessage { OriginalTransactionId = childId });
							}
						}
					}

					break;
				}
				default:
				{
					var allMsg = CheckSubscription(ref message);

					if (allMsg != null)
						base.OnInnerAdapterNewOutMessage(allMsg);
					
					break;
				}
			}

			if (message != null)
				base.OnInnerAdapterNewOutMessage(message);

			if (extra != null)
			{
				foreach (var m in extra)
					base.OnInnerAdapterNewOutMessage(m);
			}
		}

		private void ApplySubscriptionIds(ISubscriptionIdMessage message, ChildSubscription child)
		{
			var ids = message.GetSubscriptionIds();
			var initialId = child.Parent.Origin.TransactionId;
			var newIds = child.Subscribers.CachedKeys.Concat(child.Parent.Alls.CachedKeys);

			if (ids.Length == 1 && ids[0] == initialId)
				message.SetSubscriptionIds(newIds);
			else
				message.SetSubscriptionIds(ids.Where(id => id != initialId).Concat(newIds).ToArray());
		}

		private SubscriptionSecurityAllMessage CheckSubscription(ref Message message)
		{
			lock (_sync)
			{
				if (_toFlush.Count > 0)
				{
					var childs = _toFlush.CopyAndClear();
					
					foreach (var child in childs)
					{
						this.AddDebugLog("ALL flush: {0}/{1}, cnt={2}", child.Origin.SecurityId, child.Origin.DataType2, child.Suspended.Count);

						foreach (var msg in child.Suspended.CopyAndClear())
						{
							ApplySubscriptionIds(msg, child);
							RaiseNewOutMessage((Message)msg);
						}
					}
				}

				if (_parents.Count == 0)
					return null;

				if (message is ISubscriptionIdMessage subscrMsg && message is ISecurityIdMessage secIdMsg)
				{
					foreach (var parentId in subscrMsg.GetSubscriptionIds())
					{
						if (_parents.TryGetValue(parentId, out var parent))
						{
							// parent subscription has security id (not null)
							if (parent.Origin.SecurityId == secIdMsg.SecurityId)
							{
								var ids = subscrMsg.GetSubscriptionIds();
								var initialId = parent.Origin.TransactionId;
								var newIds = parent.Alls.CachedKeys.Concat(new[] { initialId });

								if (ids.Length == 1 && ids[0] == initialId)
									subscrMsg.SetSubscriptionIds(newIds);
								else
									subscrMsg.SetSubscriptionIds(ids.Where(id => id != initialId).Concat(newIds).ToArray());
							
								subscrMsg.SetSubscriptionIds();
								return null;
							}

							SubscriptionSecurityAllMessage allMsg = null;

							if (!parent.Child.TryGetValue(secIdMsg.SecurityId, out var child))
							{
								allMsg = new SubscriptionSecurityAllMessage();

								parent.Origin.CopyTo(allMsg);

								allMsg.ParentTransactionId = parentId;
								allMsg.TransactionId = TransactionIdGenerator.GetNextId();
								allMsg.SecurityId = secIdMsg.SecurityId;

								child = new ChildSubscription(parent, allMsg.TypedClone());
								child.Subscribers.Add(allMsg.TransactionId, child.Origin);

								parent.Child.Add(secIdMsg.SecurityId, child);

								allMsg.LoopBack(this, MessageBackModes.Chain);
								_pendingLoopbacks.Add(allMsg.TransactionId, RefTuple.Create(parentId, SubscriptionStates.Stopped));

								this.AddDebugLog("New ALL map: {0}/{1} TrId={2}-{3}", child.Origin.SecurityId, child.Origin.DataType2, allMsg.ParentTransactionId, allMsg.TransactionId);
							}

							if (!child.State.IsActive())
							{
								child.Suspended.Add(subscrMsg);
								message = null;

								this.AddDebugLog("ALL suspended: {0}/{1}, cnt={2}", child.Origin.SecurityId, child.Origin.DataType2, child.Suspended.Count);
							}
							else
								ApplySubscriptionIds(subscrMsg, child);

							return allMsg;
						}
					}
				}
			}

			return null;
		}

		/// <summary>
		/// Create a copy of <see cref="SubscriptionSecurityAllMessageAdapter"/>.
		/// </summary>
		/// <returns>Copy.</returns>
		public override IMessageChannel Clone()
		{
			return new SubscriptionSecurityAllMessageAdapter(InnerAdapter.TypedClone());
		}
	}
}