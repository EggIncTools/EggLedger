namespace EggLedger.Web.Data;

public interface IIndexedDb {
    ValueTask PutAsync(string store, object value);
    ValueTask<int> PutManyAsync(string store, IEnumerable<object> values);
    ValueTask<T?> GetAsync<T>(string store, object key);
    ValueTask<T[]> GetAllAsync<T>(string store);
    ValueTask<T[]> GetAllByIndexAsync<T>(string store, string index, object value);


    ValueTask<T[]> GetAllByIndexProjectedAsync<T>(string store, string index, object value);
    ValueTask DeleteAsync(string store, object key);
    ValueTask ClearAsync(string store);
    ValueTask<int> CountAsync(string store);
}
