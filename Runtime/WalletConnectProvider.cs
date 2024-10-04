using System;
using System.Numerics;
using Reown.AppKit.Unity;
using Tezos.Cysharp.Threading.Tasks;
using Tezos.MessageSystem;
using Tezos.Operation;
using Tezos.WalletProvider;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tezos.WalletConnect
{
	public class WalletConnectProvider : IAndroidProvider, IiOSProvider, IWebGLProvider
	{
		public event Action<WalletProviderData> WalletConnected;
		public event Action                     WalletDisconnected;
		public event Action<string>             PairingRequested;

		private UniTaskCompletionSource<WalletProviderData> _walletConnectionTcs;
		private UniTaskCompletionSource<bool>               _walletDisconnectionTcs;

		private WalletProviderData _walletProviderData;

		public WalletType        WalletType        => WalletType.WALLETCONNECT;
		public EvmService        EvmService        => AppKit.Evm;
		public AccountController AccountController => AppKit.AccountController;

		public async UniTask Init()
		{
			var appKitCore = Resources.Load<AppKitCore>("Reown AppKit");
			var appKit     = Object.Instantiate(appKitCore);
			Object.DontDestroyOnLoad(appKit);
			appKitCore.gameObject.hideFlags = HideFlags.HideAndDontSave;

			var walletConnectConfig = ConfigGetter.GetOrCreateConfig<WalletConnectConfig>();

			var appKitConfig = new AppKitConfig(
			                                    walletConnectConfig.ProjectId,
			                                    new Metadata(
			                                                 walletConnectConfig.Name,
			                                                 walletConnectConfig.Description,
			                                                 walletConnectConfig.Url,
			                                                 walletConnectConfig.IconUrl
			                                                )
			                                   );
			await AppKit.InitializeAsync(appKitConfig);
			AppKit.AccountConnected    += OnAccountConnected;
			AppKit.AccountDisconnected += OnAccountDisconnected;
		}

		public UniTask<string> GetBalance(string walletAddress) => UniTask.FromResult(AccountController.Balance);

		private async void OnAccountConnected(object sender, Connector.AccountConnectedEventArgs accountConnectedEventArgs)
		{
			var activeAccount = await accountConnectedEventArgs.GetAccount();

			_walletProviderData.WalletAddress = activeAccount.Address;
			_walletProviderData.PublicKey     = activeAccount.AccountId;
			_walletProviderData.WalletType    = WalletType;

			WalletConnected?.Invoke(_walletProviderData);
			_walletConnectionTcs.TrySetResult(_walletProviderData);
		}

		private void OnAccountDisconnected(object sender, Connector.AccountDisconnectedEventArgs accountDisconnectedEventArgs)
		{
			_walletProviderData = null;
			WalletDisconnected?.Invoke();
			_walletDisconnectionTcs.TrySetResult(true);
		}

		public UniTask<WalletProviderData> Connect(WalletProviderData data)
		{
			_walletProviderData  = data;
			_walletConnectionTcs = new();
			AppKit.OpenModal();
			return _walletConnectionTcs.Task;
		}

		public UniTask<bool> Disconnect()
		{
			AppKit.DisconnectAsync();
			return _walletDisconnectionTcs.Task;
		}

		public bool IsAlreadyConnected() => AppKit.IsAccountConnected;

		public UniTask RequestContractOrigination(OriginateContractRequest originationRequest) => throw new NotSupportedException("Contract origination is not supported by wallet connect.");

		public async UniTask<SignPayloadResponse> RequestSignPayload(SignPayloadRequest signRequest)
		{
			var signature = await AppKit.Evm.SignMessageAsync(signRequest.Payload);
			return new SignPayloadResponse
			       {
				       Signature = signature,
			       };
		}

		public async UniTask<OperationResponse> RequestOperation(OperationRequest operationRequest)
		{
			var transactionResult = await AppKit.Evm.SendTransactionAsync(operationRequest.Destination, BigInteger.Parse(operationRequest.Amount), operationRequest.Arg);
			return new OperationResponse
			       {
				       TransactionHash = transactionResult,
			       };
		}
	}
}