﻿using System.Diagnostics;
using CSharpWars.Common.Extensions;
using CSharpWars.Enums;
using CSharpWars.Orleans.Contracts;
using CSharpWars.Orleans.Contracts.Grains;
using CSharpWars.Orleans.Contracts.Model;
using CSharpWars.Scripting;
using Microsoft.CodeAnalysis;
using Orleans;

namespace CSharpWars.Orleans.Validation.Grains;

public class ValidationGrain : Grain, IValidationGrain
{
    private readonly IScriptCompiler _scriptCompiler;

    public ValidationGrain(
        IScriptCompiler scriptCompiler)
    {
        _scriptCompiler = scriptCompiler;
    }

    public async Task<ValidatedScriptDto> Validate(ScriptToValidateDto scriptToValidate)
    {
        var compilationStopwatch = Stopwatch.StartNew();
        var diagnostics = await _scriptCompiler.CompileForDiagnostics(scriptToValidate.Script);
        compilationStopwatch.Stop();

        var botScript = await _scriptCompiler.CompileForExecution(scriptToValidate.Script);

        var validationMessages = new List<ValidatedScriptMessageDto>();
        var runtimeInMilliseconds = long.MaxValue;

        if (!diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            var task = Task.Run(() =>
            {
                var arena = new ArenaDto("validation", 10, 10);
                var bot = new BotDto
                {
                    BotId = Guid.NewGuid(),
                    BotName = "Bot",
                    MaximumHealth = 100,
                    CurrentHealth = 100,
                    MaximumStamina = 100,
                    CurrentStamina = 100,
                    X = 1,
                    Y = 1,
                    Orientation = Orientation.South,
                    Memory = new Dictionary<string, string>().Serialize()
                };
                var friendBot = new BotDto
                {
                    BotId = Guid.NewGuid(),
                    BotName = "Friend",
                    MaximumHealth = 100,
                    CurrentHealth = 100,
                    MaximumStamina = 100,
                    CurrentStamina = 100,
                    X = 1,
                    Y = 3,
                    Orientation = Orientation.North
                };
                var enemyBot = new BotDto
                {
                    BotId = Guid.NewGuid(),
                    BotName = "Enemy",
                    MaximumHealth = 100,
                    CurrentHealth = 100,
                    MaximumStamina = 100,
                    CurrentStamina = 100,
                    X = 1,
                    Y = 5,
                    Orientation = Orientation.North
                };
                var botProperties = BotProperties.Build(bot, arena, new[] { bot, friendBot, enemyBot }.ToList());
                var scriptGlobals = ScriptGlobals.Build(botProperties);

                var runtimeStopwatch = Stopwatch.StartNew();

                try
                {
                    _ = botScript.Invoke(scriptGlobals);
                }
                catch (Exception ex)
                {
                    validationMessages.Add(new ValidatedScriptMessageDto
                    {
                        Message = "Runtime error: " + ex.Message
                    });
                }

                runtimeStopwatch.Stop();
                runtimeInMilliseconds = runtimeStopwatch.ElapsedMilliseconds;
            });

            if (!task.Wait(TimeSpan.FromSeconds(1)))
            {
                validationMessages.Add(new ValidatedScriptMessageDto
                {
                    Message = "Your script did not finish in a timely fashion!"
                });
            }
        }

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                validationMessages.Add(new ValidatedScriptMessageDto
                {
                    Message = diagnostic.GetMessage(),
                    LocationStart = diagnostic.Location.SourceSpan.Start,
                    LocationEnd = diagnostic.Location.SourceSpan.End
                });
            }
        }

        DeactivateOnIdle();

        return new ValidatedScriptDto
        {
            CompilationTimeInMilliseconds = compilationStopwatch.ElapsedMilliseconds,
            RunTimeInMilliseconds = runtimeInMilliseconds,
            ValidationMessages = validationMessages
        };
    }
}