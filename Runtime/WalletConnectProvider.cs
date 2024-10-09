using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using Beacon.Sdk.Beacon.Permission;
using Newtonsoft.Json; // todo: this needs to be removed later
using Reown.AppKit.Unity;
using Tezos.Configs;
using Tezos.Cysharp.Threading.Tasks;
using Tezos.Logger;
using Tezos.MainThreadDispatcher;
using Tezos.MessageSystem;
using Tezos.Operation;
using Tezos.Request;
using Tezos.WalletProvider;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tezos.WalletConnect
{
	public class WalletConnectProvider : IAndroidProvider, IiOSProvider, IWebGLProvider
	{
		public class JsonRpcResponse
		{
			[JsonProperty("jsonrpc")] public string JsonRpcVersion { get; set; }

			[JsonProperty("result")] public string Result { get; set; }

			[JsonProperty("id")] public int Id { get; set; }
		}

		public class JsonRpcPayload
		{
			[JsonProperty("jsonrpc")] public string JsonRpcVersion { get; set; } = "2.0";

			[JsonProperty("method")] public string Method { get; set; } = "eth_getBalance";

			[JsonProperty("params")] public string[] Params { get; set; }

			[JsonProperty("id")] public int Id { get; set; } = 1;
		}

		private static readonly Chain ETHERLINK_TESTNET = new(
		                                                      ChainConstants.Namespaces.Evm,
		                                                      "128123",
		                                                      "Etherlink Testnet",
		                                                      new Currency("Tez", "XTZ", 6),
		                                                      new BlockExplorer("Explorer", "https://testnet.explorer.etherlink.com/"),
		                                                      "https://node.ghostnet.etherlink.com",
		                                                      true,
		                                                      "https://etherlink.com/opengraph-image.png?4dd162b94a289c06",
		                                                      "etherlink-testnet"
		                                                     );

		private static readonly Chain ETHERLINK_MAINNET = new(
		                                                      ChainConstants.Namespaces.Evm,
		                                                      "42793",
		                                                      "Etherlink Mainnet",
		                                                      new Currency("Tez", "XTZ", 6),
		                                                      new BlockExplorer("Explorer", "https://mainnet.explorer.etherlink.com/"),
		                                                      "https://node.mainnet.etherlink.com",
		                                                      false,
		                                                      "https://etherlink.com/opengraph-image.png?4dd162b94a289c06",
		                                                      "etherlink-mainnet"
		                                                     );

		private Chain _selectedChain;

		public event Action<WalletProviderData> WalletConnected;
		public event Action                     WalletDisconnected;
		public event Action<string>             PairingRequested;

		private UniTaskCompletionSource<WalletProviderData> _walletConnectionTcs;
		private UniTaskCompletionSource<bool>               _walletDisconnectionTcs;
		private WalletProviderData                          _walletProviderData;
		private Rpc                                         _rpc;

		public WalletType        WalletType        => WalletType.WALLETCONNECT;
		public EvmService        EvmService        => AppKit.Evm;
		public AccountController AccountController => AppKit.AccountController;
		public NetworkController NetworkController => AppKit.NetworkController;

		public async UniTask Init()
		{
			_rpc = new(5);
			var appKitCore = Resources.Load<AppKitCore>("Reown AppKit");
			var appKit     = Object.Instantiate(appKitCore);
			Object.DontDestroyOnLoad(appKit);
			appKit.gameObject.hideFlags = HideFlags.HideAndDontSave;

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

			var network = ConfigGetter.GetOrCreateConfig<DataProviderConfig>().Network;
			if (network      == NetworkType.ghostnet) _selectedChain = ETHERLINK_TESTNET;
			else if (network == NetworkType.mainnet) _selectedChain  = ETHERLINK_MAINNET;
			else throw new NotSupportedException($"Network {network} is not supported in wallet connect");

			var supportedChains = new List<Chain>(appKitConfig.supportedChains)
			                      {
				                      ETHERLINK_MAINNET,
				                      ETHERLINK_TESTNET
			                      };

			appKitConfig.supportedChains = supportedChains.ToArray();

			await AppKit.InitializeAsync(appKitConfig);
			TezosLogger.LogInfo($"Wallet connect IsAccountConnected:{AppKit.IsAccountConnected}");
			AppKit.AccountConnected    += OnAccountConnected;
			AppKit.AccountDisconnected += OnAccountDisconnected;
		}

		public async UniTask<string> GetBalance(string walletAddress)
		{
			TezosLogger.LogInfo($"Wallet connect provider getting balance for {walletAddress}");
			var    payload         = new JsonRpcPayload { Params = new[] { walletAddress } };
			var    jsonRpcResponse = await _rpc.PostRequest<JsonRpcResponse>(Path.Combine(_selectedChain.RpcUrl), payload);
			string balance         = String.Empty;
			try
			{
				balance = jsonRpcResponse.Result.Substring(2);
				balance = BigInteger.Parse(balance, NumberStyles.HexNumber).ToString().Substring(0, 6);
			}
			catch (Exception e)
			{
				TezosLogger.LogWarning($"Failed to parse balance string, probably no balance found. Exception: {e}");
			}

			TezosLogger.LogInfo($"Wallet connect {walletAddress} balance: {balance}");
			return balance;
		}

		private async void OnAccountConnected(object sender, Connector.AccountConnectedEventArgs accountConnectedEventArgs)
		{
			var activeAccount = await accountConnectedEventArgs.GetAccount();

			_walletProviderData.WalletAddress = activeAccount.Address;
			_walletProviderData.PublicKey     = activeAccount.AccountId;
			_walletProviderData.WalletType    = WalletType;

			WalletConnected?.Invoke(_walletProviderData);
			_walletConnectionTcs?.TrySetResult(_walletProviderData);
		}

		private void OnAccountDisconnected(object _, Connector.AccountDisconnectedEventArgs __)
		{
			_walletProviderData = null;
			WalletDisconnected?.Invoke();
			_walletDisconnectionTcs?.TrySetResult(true);
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
			_walletDisconnectionTcs = new();

			if (!AppKit.IsAccountConnected)
			{
				OnAccountDisconnected(null, null);
				return _walletDisconnectionTcs.Task;
			}

			AppKit.DisconnectAsync();
			return _walletDisconnectionTcs.Task;
		}

		public bool IsAlreadyConnected() => AppKit.IsAccountConnected;

		public UniTask DeployContract(DeployContractRequest originationRequest) => throw new NotSupportedException("Contract origination is not supported by wallet connect.");

		public async UniTask<SignPayloadResponse> RequestSignPayload(SignPayloadRequest signRequest)
		{
			var signature = await EvmService.SignMessageAsync(signRequest.Payload);
			return new SignPayloadResponse
			       {
				       Signature = signature,
			       };
		}

		public async UniTask<OperationResponse> RequestOperation(OperationRequest operationRequest)
		{
			var transactionResult = await EvmService.WriteContractAsync(operationRequest.Destination, operationRequest.ContractABI, operationRequest.EntryPoint, operationRequest.Arg);
			return new OperationResponse
			       {
				       TransactionHash = transactionResult,
			       };
		}
	}
}