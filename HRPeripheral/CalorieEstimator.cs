namespace HRPeripheral;

/// <summary>
/// Simple helper class that estimates **calories burned per minute**
/// based on heart rate, gender, weight, and age.
///
/// Uses the **ACSM (American College of Sports Medicine)**–style regression formulas
/// for caloric expenditure derived from laboratory studies.
///
/// The returned value is in **kilocalories per minute (kcal/min)**.
///
/// References:
///   - Key study: Keytel LR et al. “Prediction of energy expenditure from heart rate monitoring during submaximal exercise”
///     (Eur J Appl Physiol, 2005)
///   - Commonly used by Polar, Garmin, and other HRM-based fitness apps.
/// </summary>
public class CalorieEstimator
{
    // ================================================================
    // INSTANCE FIELDS
    // ================================================================
    // These are the personal parameters the formula depends on:
    readonly bool _male;   // True = male formula, False = female
    readonly double _kg;   // Body weight in kilograms
    readonly int _age;     // Age in years

    // ================================================================
    // CONSTRUCTOR
    // ================================================================
    /// <summary>
    /// Initializes a new CalorieEstimator instance with the user's
    /// biological sex, body weight, and age.
    /// </summary>
    /// <param name="male">True if male, false if female.</param>
    /// <param name="weightKg">Body weight in kilograms.</param>
    /// <param name="age">Age in years.</param>
    public CalorieEstimator(bool male, double weightKg, int age)
    {
        _male = male;
        _kg = weightKg;
        _age = age;
    }

    // ================================================================
    // QUICK FACTORY PRESET
    // ================================================================
    /// <summary>
    /// Convenience factory method that returns a default estimator
    /// for a "typical" male: 75 kg, 35 years old.
    /// Useful for testing or when user data is not available.
    /// </summary>
    public static CalorieEstimator DefaultMale75kgAge35()
        => new CalorieEstimator(true, 75, 35);

    // ================================================================
    // CALORIC EXPENDITURE ESTIMATE
    // ================================================================
    /// <summary>
    /// Estimates energy expenditure in **kilocalories per minute** using
    /// the heart rate method (empirical ACSM regression).
    ///
    /// Formula (men):
    ///   kcal/min = (-55.0969 + 0.6309 × HR + 0.1988 × weight_kg + 0.2017 × age) / 4.184
    ///
    /// Formula (women):
    ///   kcal/min = (-20.4022 + 0.4472 × HR - 0.1263 × weight_kg + 0.074 × age) / 4.184
    ///
    /// where:
    ///   • HR = heart rate in beats per minute (bpm)
    ///   • 4.184 converts Joules to kilocalories
    ///
    /// Note:
    ///   - These are approximations based on population averages.
    ///   - Accuracy decreases at very low or very high heart rates.
    /// </summary>
    /// <param name="hr">Heart rate (beats per minute).</param>
    /// <returns>Estimated energy expenditure in kcal/min.</returns>
    public double KcalPerMinute(int hr)
    {
        if (_male)
        {
            // Male regression equation
            return (-55.0969 + 0.6309 * hr + 0.1988 * _kg + 0.2017 * _age) / 4.184;
        }
        else
        {
            // Female regression equation
            return (-20.4022 + 0.4472 * hr - 0.1263 * _kg + 0.074 * _age) / 4.184;
        }
    }
}
