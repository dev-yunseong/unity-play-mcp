using System;

namespace Artel.Affordances
{
    /// <summary>
    /// Points a type at its evidence, which lives in a resource on the same assembly.
    /// </summary>
    /// <remarks>
    /// This used to carry the evidence itself, one attribute per record. Measured across three
    /// projects that made the game assembly three to eight times its own size, and which of those
    /// it landed on had nothing to do with how big the game was. Ninety-eight percent of the growth
    /// was JSON text.
    ///
    /// It carries an anchor now because an attribute is the one thing that survives renaming — it
    /// stays attached to whatever its type became, so an obfuscated build can still be joined up.
    /// The evidence itself is in a resource, which is not metadata, is not parsed until asked for,
    /// and compresses to a fraction of what the same text cost as attributes.
    ///
    /// The anchor is not the only way in. Managed stripping set to High removes custom attributes
    /// entirely — measured, and it took this one with it — while leaving resources alone. So the
    /// resource also records each type's name, and the scan falls back to matching on that. One of
    /// the two survives each of the two treatments; a build that both strips hard and obfuscates
    /// defeats both, and says so rather than reporting an empty game.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AffordanceAttribute : Attribute
    {
        public AffordanceAttribute(int schemaVersion, int anchor)
        {
            SchemaVersion = schemaVersion;
            Anchor = anchor;
        }

        public int SchemaVersion { get; }

        /// <summary>Which entry in the assembly's evidence resource belongs to this type.</summary>
        public int Anchor { get; }
    }
}
