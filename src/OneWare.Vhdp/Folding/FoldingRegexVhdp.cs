using System.Text.RegularExpressions;

namespace OneWare.Vhdp.Folding;

public partial class FoldingRegexVhdp
{
    private const string FoldingStartPattern = """
                                               (?x)
                                               		# From the start of the line make sure we are not going into a comment ...
                                               		^(
                                               			([^-]-?(?!-))*?
                                               				(
                                               				# Check for keyword ... is
                                               				# (\b(?i:architecture|case|entity|function|package|procedure)\b(.+?)(?i:\bis)\b)
                                               
                                               				# Check for if statements
                                               				# |(\b(?i:if)\b(.+?)(?i:generate|then)\b)
                                               
                                               				# Check for and while statements
                                               				# |(\b(?i:for|while)(.+?)(?i:loop|generate)\b)
                                               
                                               				# Check for keywords that do not require an is after it
                                               				# |(\b(?i:component|process|record)\b[^;]*?$)
                                               
                                               				# From the beginning of the line, check for instantiation maps
                                               				# |(^\s*\b(?i:port|generic)\b(?i:\s+map\b)?\s*\()
                                               				
                                               				# Check for /*
                                               				  (/\*)
                                               				 
                                               				# Check for {
                                               				  |({)
                                               				  
                                                            # Check for (
                                                              |(\()
                                               			)
                                               		)
                                               	
                                               """;

    private const string FoldingEndPattern = """
                                             (?x)
                                             # From the start of the line make sure we are not going into a comment ...
                                             ^(
                                             	([^-]-?(?!-))*?
                                             		(
                                             		# Check for keyword ... is
                                             		# (\b(?i:architecture|case|entity|function|package|procedure)\b(.+?)(?i:\bis)\b)
                                             
                                             		# Check for if statements
                                             		# |(\b(?i:if)\b(.+?)(?i:generate|then)\b)
                                             
                                             		# Check for and while statements
                                             		# |(\b(?i:for|while)(.+?)(?i:loop|generate)\b)
                                             
                                             		# Check for keywords that do not require an is after it
                                             		# |(\b(?i:component|process|record)\b[^;]*?$)
                                             
                                             		# From the beginning of the line, check for instantiation maps
                                             		# |(^\s*\b(?i:port|generic)\b(?i:\s+map\b)?\s*\()
                                             		
                                             		# Check for */
                                                      (\*\/)
                                             
                                             		# Check for {
                                             		  |(})
                                             		  
                                                    # Check for )
                                                      |(\))
                                             	)
                                             )
                                             
                                             """;
    
    [GeneratedRegex(FoldingStartPattern, RegexOptions.Multiline)]
    public static partial Regex FoldingStart();

    [GeneratedRegex(FoldingEndPattern, RegexOptions.Multiline)]
    public static partial Regex FoldingEnd();
}