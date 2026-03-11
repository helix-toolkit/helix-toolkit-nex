namespace HelixToolkit.Nex.ECS;

public interface ISortable<T>
{
    /// <summary>
    /// To sort by ascending order, return this.xxx < obj.xxx.
    /// To sort by descending order, return this.xxx > obj.xxx.
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    bool Compare(ref T obj);
}
