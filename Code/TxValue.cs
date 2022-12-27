using Newtonsoft.Json;


class TxValue
{
    public static List<Tx> FilterTxWithLessThanMinswap(List<Tx> list, string network,
            int? minSwap)
    {
        PriceBank.stables = new();

        string[] stablesContractsInTheNetwork = Stable.ArrayContractsInNetwork(network);
        List<Stable> stableList = Stable.ListAllInNetwork(network);
        var networks = Network.AllNetworks();


        using (HttpClient client = new())
        {
            for (int i = 0; i < list.Count();)
            {
                Tx tx = list.ElementAt(i);

                bool includesStableSwap = false;
                bool stableSwapMoreThanMinswap = false;
                int j = i;

                bool approved = false;

                while (list.ElementAt(j).Txhash == tx.Txhash)
                {
                    //if true, then skip to the next tx, dont check twice the same tx
                    if (approved)
                    {
                        j++;
                        continue;
                    }

                    //make the price only update once per run. so if there are 900tx with bnb to not update it 900 times
                    if (stablesContractsInTheNetwork.Any(list.ElementAt(j).ContractAddress.ToLower().Contains))
                    {
                        includesStableSwap = true;

                        string tokenAddress = list.ElementAt(j).ContractAddress;
                        double tokenPrice = TokenPrice(tokenAddress, stableList, networks, client);

                        if (double.Parse(list.ElementAt(j).TokenValue) * tokenPrice >= minSwap)
                        {
                            stableSwapMoreThanMinswap = true;
                            approved = true;
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
        }

        return list;
    }

    private static double TokenPrice(string tokenAddress, List<Stable> stableList, IList<Network> networks, HttpClient client)
    {
        Stable token = stableList.First(t => t.contract.ToLower() == tokenAddress.ToLower());

        //if it's a stable
        if (token.usdEqual)
            return 1;

        //if it's already on the list
        var stable = PriceBank.stables.FirstOrDefault(s => s.contract.ToLower() == tokenAddress.ToLower());
        if (stable != null)
            return stable.price;

        return GetPrice(token, networks, client).Result;
    }

    private static async Task<double> GetPrice(Stable token, IList<Network> networks, HttpClient client)
    {
        // https://api.bscscan.com/api
        //     ?module=stats
        //     &action=bnbprice
        //     &apikey=YourApiKeyToken

        string baseUrl = "";
        Network network = new();

        switch (token.name.ToLower())
        {
            case "bnb":
            case "wbnb":
                network = networks.First(n => n.Name == "BSC");
                baseUrl = $"{network.Api}?module=stats&action="; baseUrl += "bnbprice";
                baseUrl += "&apikey=" + network.Key;
                break;
            case "matic":
            case "wmatic":
                network = networks.First(n => n.Name == "Polygon");
                baseUrl = $"{network.Api}?module=stats&action="; baseUrl += "maticprice";
                baseUrl += "&apikey=" + network.Key;
                break;
            case "eth":
            case "weth":
                network = networks.First(n => n.Name == "Ethereum");
                baseUrl = $"{network.Api}?module=stats&action="; baseUrl += "ethprice";
                baseUrl += "&apikey=" + network.Key;
                break;
            default: // if the token was not eth bnb or matic, for some reason //10001
                Console.WriteLine("10001 Didn't find the token. Couldn't check price. Price is 1(default)\n" +
                $"{token.name}, {token.contract}, {token.network}, {network.Name}");
                return 1;
        }

        CoinPriceResponse result = new();

        for (int i = 0; i < 3; i++)
        {
            try
            {
                var response = await client.GetAsync(baseUrl);

                string reader = await response.Content.ReadAsStringAsync();

                result = JsonConvert.DeserializeObject<CoinPriceResponse>(reader);

                break;
            }
            catch (Exception exc)
            {
                Console.WriteLine(exc);
                Thread.Sleep(1000);
            }
        }

        if (result.status == null)
        {
            //10002
            Console.WriteLine("10002 Didn't find the token. Couldn't check price. Price is 1(default)\n" +
                $"{token.name}, {token.contract}, {token.network}, {network.Name}");
                return 1;
        }

        string strPrice = result.result.bnbusd != null ? result.result.bnbusd :
            (result.result.ethusd != null ? result.result.ethusd : result.result.maticusd);
        double price = double.Parse(strPrice);

        //add this token in the bank
        PriceBank.stables.Add(new PriceBank.StableInBank(token, price));

        return price;
    }
}

public class PriceBank
{
    //the idea is, that here i will store token prices
    //the token is added here when it's first met
    //price is parsed from api
    //then, everytime i meet it again i take price from here

    public static List<StableInBank> stables { get; set; }
    public class StableInBank
    {
        public string name { get; set; }
        public string contract { get; set; }
        public string network { get; set; }
        public bool usdEqual { get; set; }
        public double price { get; set; }
        public StableInBank(Stable stable, double price)
        {
            this.name = stable.name;
            this.contract = stable.contract;
            this.network = stable.network;
            this.usdEqual = stable.usdEqual;
            this.price = price;
        }
    }
}

public class CoinPriceResponse
{
    public string status { get; set; }
    public Prices result { get; set; }
    public class Prices
    {
        public string ethusd { get; set; }
        public string bnbusd { get; set; }
        public string maticusd { get; set; }
    }

    // {
    //   "status": "1",
    //   "message": "OK",
    //   "result": {
    //     "maticbtc": "0.0000481524788827102",
    //     "maticbtc_timestamp": "1672156391",
    //     "maticusd": "0.8088",
    //     "maticusd_timestamp": "1672156379"
    //   }
    // }
}

class Network
{
    public string Name { get; set; }
    public string Api { get; set; }
    public string Key { get; set; }
    public static List<Network> AllNetworks()
    {
        return JsonConvert.DeserializeObject<List<Network>>(File.ReadAllText("networks.json"));
    }

}

public class Stable
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