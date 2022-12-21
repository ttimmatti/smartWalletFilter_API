using Newtonsoft.Json;


class TxValue
{
    public static List<Tx> FilterTxWithLessThanMinswap(List<Tx> list, string networkForStables,
            int? minSwap)
    {
        string[] stablesContractsInTheNetwork = Stable.ArrayContractsInNetwork(networkForStables);

        for (int i = 0; i < list.Count();)
        {
            Tx tx = list.ElementAt(i);

            bool includesStableSwap = false;
            bool stableSwapMoreThanMinswap = false;
            int j = i;

            while (list.ElementAt(j).Txhash == tx.Txhash)
            {
                if (stablesContractsInTheNetwork.Any(list.ElementAt(j).ContractAddress.ToLower().Contains))
                {
                    includesStableSwap = true;

                    if (double.Parse(list.ElementAt(j).TokenValue) >= minSwap)
                    {
                        stableSwapMoreThanMinswap = true;
                    }
                }

                j++;
                if (j >= list.Count())
                    break;
            }

            if (!stableSwapMoreThanMinswap)
            {
                j -= list.RemoveAll(x => x.Txhash == tx.Txhash);
            }

            i = j;
        }

        return list;
    }
}

class NativePrice
{
    public static async Task<double> GetPrice(Stable stable, string network)
    {
        IList<Network> networks = Network.AllNetworks();
        Network currNetwork = networks.First(x => x.Name == network);

        string baseUrl = $"{currNetwork.Api}?module=stats&action=";

        switch (stable.name.ToLower())
        {
            case "bnb":
                baseUrl += "bnbprice";
                break;
            case "matic":
                baseUrl += "maticprice";
                break;
            case "eth":
                baseUrl += "ethprice";
                break;
            default: // if the token was not eth bnb or matic, for some reason
                return 1;
        }

        return 1; // not working !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
    }

    // https://api.bscscan.com/api
    //     ?module=stats
    //     &action=bnbprice
    //     &apikey=YourApiKeyToken
}

class Network
{
    public string Name { get; set; }
    public string Api { get; set; }
    public string Key { get; set; }
    public static IList<Network> AllNetworks()
    {
        return JsonConvert.DeserializeObject<List<Network>>(File.ReadAllText("networks.json"));
    }
    
}

class Stable
{
    public string name { get; set; }
    public string contract { get; set; }
    public string network { get; set; }
    public bool usdEqual { get; set; }
    
    //returns list of stables in the passed in network($name, $contract)
    public static List<Stable> ListAllInNetwork(string network)
    {
        List<Stable> list = JsonConvert.DeserializeObject<List<Stable>>(
            File.ReadAllText("stables.json")
        );

        list.RemoveAll(x => x.network != network);

        return list;
    }

    //returns array of strings that consists only the contract addresses of stables
    // in the passed in network
    public static string[] ArrayContractsInNetwork(string network)
    {
        IList<Stable> list = ListAllInNetwork(network);

        string[] array = new string[list.Count()];

        for (int i = 0; i < array.Length; i++)
        {
            array[i] = list.ElementAt(i).contract.ToLower();
        }

        return array;
    }
}