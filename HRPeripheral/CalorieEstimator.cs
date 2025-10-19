namespace HRPeripheral;

public class CalorieEstimator
{
    readonly bool _male;
    readonly double _kg;
    readonly int _age;

    public CalorieEstimator(bool male, double weightKg, int age) { _male = male; _kg = weightKg; _age = age; }

    public static CalorieEstimator DefaultMale75kgAge35() => new CalorieEstimator(true, 75, 35);

    // Returns kcal per minute (HR method; ACSM-style approximation)
    public double KcalPerMinute(int hr)
    {
        if (_male)
            return (-55.0969 + 0.6309 * hr + 0.1988 * _kg + 0.2017 * _age) / 4.184;
        else
            return (-20.4022 + 0.4472 * hr - 0.1263 * _kg + 0.074 * _age) / 4.184;
    }
}