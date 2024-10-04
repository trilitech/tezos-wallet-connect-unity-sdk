using Tezos.Editor;
using Tezos.MessageSystem;
using UnityEditor;
using UnityEngine;

namespace Tezos.WalletConnect.Editor
{
	public class WalletConnectConfigEditor : ITezosEditor
	{
		public void SetupConfigs()
		{
			var walletConnectConfig = ConfigGetter.GetOrCreateConfig<WalletConnectConfig>();

			EditorUtility.SetDirty(walletConnectConfig);
			AssetDatabase.SaveAssetIfDirty(walletConnectConfig);
			AssetDatabase.Refresh();
			
			Debug.Log("Tezos wallet connect configs created.");
		}
	}
}