using UnityEngine;

namespace Tezos.WalletConnect
{
	[CreateAssetMenu(fileName = "WalletConnectConfig", menuName = "Tezos/Configuration/WalletConnectConfig", order = 1)]
	public class WalletConnectConfig : ScriptableObject
	{
		public string ProjectId   = "e8fb22e1cf5233d73d6ea89c8d702562";
		public string Name        = "tezos-test";
		public string Description = "Project Description";
		public string Url         = "https://example.com";
		public string IconUrl     = "https://example.com/logo.png";
	}
}