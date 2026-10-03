namespace Ankus.Generators;

/// <summary>
/// Contains the complete detached declaration contracts needed to compose one extension.
/// </summary>
/// <param name="Methods">The method inventory and cached role-specific artifacts.</param>
/// <param name="Schemas">The schema declarations.</param>
/// <param name="Providers">The explicit SQL ownership contracts.</param>
/// <param name="CustomBlocks">The resolved custom SQL blocks.</param>
/// <param name="Settings">The compiler-visible extension settings.</param>
/// <param name="Enums">The enum declarations and cached artifacts.</param>
/// <param name="Aggregates">The aggregate contracts and support artifacts.</param>
/// <param name="Properties">The configuration and native callback properties.</param>
/// <param name="Prefixes">The reserved configuration prefixes.</param>
/// <param name="CustomTypes">The custom storage declarations and artifacts.</param>
/// <param name="Mappings">The datum registrations and derived operators.</param>
/// <param name="References">The typed SQL dependency contracts.</param>
/// <param name="Module">The native module identity and diagnostics.</param>
/// <param name="NativeCompilation">The detached assembly and ABI capabilities.</param>
internal sealed record ExtensionCompositionInput(FunctionPipeline.MethodInputs Methods,
    EquatableArray<SchemaPipeline.SchemaOutput> Schemas, EquatableArray<SqlProviderModel> Providers,
    EquatableArray<CustomSqlPipeline.Output> CustomBlocks, (string Directory, bool IncludeTests, string? Version) Settings,
    EquatableArray<EnumPipeline.EnumOutput> Enums, EquatableArray<AggregatePipeline.Output> Aggregates,
    GucPipeline.PropertyInputs Properties, GucPrefixPipeline.Output Prefixes, EquatableArray<CustomTypePipeline.Output> CustomTypes,
    DatumPipeline.Output Mappings, EquatableArray<SqlReferenceModel> References, NativeModuleMagic.ModuleOutput Module,
    NativeCompilationPipeline.Output NativeCompilation)
{
    /// <summary>
    /// Enumerates only attribution coordinates that deterministic composition can consult.
    /// </summary>
    /// <returns>The declaration and diagnostic coordinates, including independently located support roles.</returns>
    internal IEnumerable<GeneratorLocation?> Locations()
    {
        foreach (MethodInventoryModel method in Methods.Methods)
        {
            yield return method.Location;
        }

        foreach (FunctionPipeline.FunctionOutput output in Methods.Functions)
        {
            yield return output.Analysis.Location;
            foreach (GeneratorProblem problem in output.Analysis.Problems)
            {
                yield return problem.Location;
            }
        }

        foreach (TriggerPipeline.TriggerOutput output in Methods.Triggers)
        {
            yield return output.Analysis.Location;
            foreach (GeneratorProblem problem in output.Analysis.Problems)
            {
                yield return problem.Location;
            }
        }

        foreach (BackgroundWorkerPipeline.WorkerOutput output in Methods.Workers)
        {
            yield return output.Analysis.Location;
            foreach (GeneratorProblem problem in output.Analysis.Problems)
            {
                yield return problem.Location;
            }
        }

        foreach (LifecyclePipeline.LifecycleOutput output in Methods.Lifecycle)
        {
            yield return output.Analysis.Location;
            foreach (GeneratorProblem problem in output.Analysis.Problems)
            {
                yield return problem.Location;
            }
        }

        foreach (OperatorCastPipeline.Output output in Methods.OperatorCasts)
        {
            yield return output.Analysis.Location;
            foreach (GeneratorProblem problem in output.Analysis.Problems)
            {
                yield return problem.Location;
            }
        }

        foreach (PgTestPipeline.TestOutput output in Methods.Tests.Tests)
        {
            yield return output.Analysis.Location;
            foreach (GeneratorProblem problem in output.Analysis.Problems)
            {
                yield return problem.Location;
            }
        }

        foreach (SchemaPipeline.SchemaOutput output in Schemas)
        {
            yield return output.Analysis.Location;
            yield return output.Analysis.Problem?.Location;
        }

        foreach (SqlProviderModel provider in Providers)
        {
            yield return provider.Location;
        }

        foreach (CustomSqlPipeline.Output output in CustomBlocks)
        {
            yield return output.Analysis.Location;
        }

        foreach (EnumPipeline.EnumOutput output in Enums)
        {
            yield return output.Analysis.Location;
        }

        foreach (AggregatePipeline.Output output in Aggregates)
        {
            yield return output.Analysis.Location;
            foreach (AggregatePipeline.HelperAnalysis helper in output.Analysis.Helpers)
            {
                yield return helper.Location;
            }

            foreach (GeneratorProblem problem in output.Analysis.Problems)
            {
                yield return problem.Location;
            }
        }

        foreach (GucPipeline.Output output in Properties.Settings)
        {
            yield return output.Analysis.Location;
            foreach (GeneratorProblem problem in output.Analysis.Problems)
            {
                yield return problem.Location;
            }
        }

        foreach (NativeCallbackPipeline.Output output in Properties.Callbacks)
        {
            foreach (GeneratorProblem problem in output.Analysis.Problems)
            {
                yield return problem.Location;
            }
        }

        foreach (GeneratorProblem problem in Prefixes.Analysis.Problems)
        {
            yield return problem.Location;
        }

        foreach (CustomTypePipeline.Output output in CustomTypes)
        {
            yield return output.Analysis.Location;
        }

        foreach (DatumTypeModel mapping in Mappings.Analysis.Models)
        {
            yield return mapping.Location;
        }

        foreach (DerivedOperatorModel model in Mappings.Analysis.Derived)
        {
            yield return model.Location;
        }

        foreach (GeneratorProblem problem in Mappings.Analysis.Problems)
        {
            yield return problem.Location;
        }

        foreach (SqlReferenceModel reference in References)
        {
            yield return reference.Location;
        }

        yield return Module.NameError?.Location;
        yield return Module.VersionError?.Location;
    }
}
