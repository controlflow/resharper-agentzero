using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Resolve;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Psi.Util;
using JetBrains.Util.Logging;
using Microsoft.Z3;

namespace JetBrains.ReSharper.Plugin.AgentZero.Analysis
{
  // todo: nullable arithmetics
  // todo: checked arithmetics
  // todo: implicit conversions
  // todo: sbyte/byte have no some operators, widened to int
  // todo: casts
  // todo: detect non-linear math
  // todo: static methods from Double.*, Single.*, Math.*

  public sealed class ExpressionsRewriter : TreeNodeVisitor<RewriteContext, Expr>, IDisposable
  {
    private readonly Context myContext;
    private readonly Dictionary<IDeclaredElement, Expr> myFreeVariables = new Dictionary<IDeclaredElement, Expr>();
    private readonly PredefinedType myPredefinedType;

    private readonly CSharpPredefined myPredefined;
    [CanBeNull] private FPRMExpr myRoundingMode;


    public ExpressionsRewriter([NotNull] PredefinedType predefinedType)
    {
      myContext = new Context();
      myPredefinedType = predefinedType;
      myPredefined = CSharpPredefined.GetInstance(predefinedType.Module);
    }

    public override Expr VisitNode(ITreeNode node, RewriteContext context)
    {
      var expression = node as ICSharpExpression;
      if (expression != null)
      {
        var constantValue = expression.ConstantValue;
        if (constantValue.IsBadValue()) return null;

        var convertible = constantValue.Value as IConvertible;
        if (convertible == null) return null;

        return ConstantFromValue(convertible);
      }

      return null;
    }

    [CanBeNull, Pure]
    private Sort GetSort([NotNull] IType type)
    {
      var declaredType = type as IDeclaredType;
      if (declaredType == null) return null;

      var enumerationType = type.GetTypeElement<IEnum>();
      if (enumerationType != null)
      {
        return GetSort(enumerationType.GetUnderlyingType());
      }

      if (myPredefinedType.Bool.Equals(declaredType))
        return myContext.MkBoolSort();

      if (declaredType.Equals(myPredefinedType.Byte) ||
          declaredType.Equals(myPredefinedType.Sbyte))
        return myContext.MkBitVecSort(size: 8);

      if (declaredType.Equals(myPredefinedType.Short) ||
          declaredType.Equals(myPredefinedType.Ushort) ||
          declaredType.Equals(myPredefinedType.Char))
        return myContext.MkBitVecSort(size: 16);

      if (declaredType.Equals(myPredefinedType.Int) ||
          declaredType.Equals(myPredefinedType.Uint))
        return myContext.MkBitVecSort(size: 32);

      if (declaredType.Equals(myPredefinedType.Long) ||
          declaredType.Equals(myPredefinedType.Ulong))
        return myContext.MkBitVecSort(size: 64);

      if (declaredType.Equals(myPredefinedType.Float))
        return myContext.MkFPSortSingle();

      if (declaredType.Equals(myPredefinedType.Double))
        return myContext.MkFPSortDouble();

      return null;
    }

    [CanBeNull, Pure]
    private Expr ConstantFromValue([NotNull] IConvertible convertible)
    {
      switch (convertible.GetTypeCode())
      {
        case TypeCode.Boolean:
          return myContext.MkBool((bool) convertible);
        case TypeCode.Char:
          return myContext.MkBV((uint) (char) convertible, size: 16);
        case TypeCode.Byte:
          return myContext.MkBV((byte) convertible, size: 8);
        case TypeCode.SByte:
          return myContext.MkBV((sbyte) convertible, size: 8);
        case TypeCode.Int16:
          return myContext.MkBV((short) convertible, size: 16);
        case TypeCode.UInt16:
          return myContext.MkBV((uint) (ushort) convertible, size: 16);
        case TypeCode.Int32:
          return myContext.MkBV((int) convertible, size: 32);
        case TypeCode.UInt32:
          return myContext.MkBV((uint) convertible, size: 32);
        case TypeCode.Int64:
          return myContext.MkBV((long) convertible, size: 64);
        case TypeCode.UInt64:
          return myContext.MkBV((ulong) convertible, size: 64);
        case TypeCode.Single:
          return myContext.MkFP((float) convertible, myContext.MkFPSortSingle());
        case TypeCode.Double:
          return myContext.MkFP((double) convertible, myContext.MkFPSortDouble());
        default:
          return null;
      }
    }

    [NotNull]
    private FPRMExpr GlobalRoundingMode
    {
      get
      {
        if (myRoundingMode != null) return myRoundingMode;

        var roundingModeSort = myContext.MkFPRoundingModeSort();
        var roundingMode = (FPRMExpr) myContext.MkConst("roundingmode", roundingModeSort);

        return myRoundingMode = roundingMode;
      }
    }

    public override Expr VisitReferenceExpression(IReferenceExpression referenceExpression, RewriteContext context)
    {
      var resolveResult = referenceExpression.Reference.Resolve();

      // todo: fields as variables?
      // todo: constants
      var typeOwner = resolveResult.DeclaredElement as ITypeOwner;
      if (typeOwner is ILocalVariable || typeOwner is IParameter)
      {
        var variableType = typeOwner.Type;

        Expr value;
        if (myFreeVariables.TryGetValue(typeOwner, out value)) return value;

        var variableSort = GetSort(variableType);
        if (variableSort == null) return null;

        var constExpression = myContext.MkConst(typeOwner.ShortName, variableSort);
        return constExpression;
      }

      return null;
    }

    public override Expr VisitBinaryExpression(IBinaryExpression binaryExpression, RewriteContext context)
    {
      var binaryConstant = base.VisitBinaryExpression(binaryExpression, context);
      if (binaryConstant != null) return binaryConstant;

      var operatorReference = binaryExpression.OperatorReference;
      if (operatorReference == null) return null;

      var resolveResult = operatorReference.Resolve();
      if (resolveResult.ResolveErrorType != ResolveErrorType.OK) return null;

      var signOperator = resolveResult.DeclaredElement as IOperator;
      if (signOperator == null || !signOperator.IsPredefined) return null;

      var returnType = signOperator.ReturnType;

      if (returnType.Equals(myPredefinedType.Bool))
        return VisitBooleanBinaryOperator(binaryExpression, context, signOperator);

      if (returnType.Equals(myPredefinedType.Int) ||
          returnType.Equals(myPredefinedType.Uint) ||
          returnType.Equals(myPredefinedType.Long) ||
          returnType.Equals(myPredefinedType.Ulong))
        return VisitIntegerBinaryOperator(binaryExpression, context, signOperator);

      if (returnType.Equals(myPredefinedType.Float) ||
          returnType.Equals(myPredefinedType.Double))
        return VisitFloatingBinaryOperator(binaryExpression, context, signOperator);

      Logger.LogError("Unknown predefined operator: {0}", signOperator);
      return null;
    }

    [CanBeNull, Pure]
    private BitVecExpr VisitIntegerBinaryOperator(
      [NotNull] IBinaryExpression binaryExpression, [NotNull] RewriteContext context, [NotNull] IOperator signOperator)
    {
      var leftExpr = (BitVecExpr) AcceptExpression(binaryExpression.LeftOperand, context);
      if (leftExpr == null) return null;

      var rightExpr = (BitVecExpr) AcceptExpression(binaryExpression.RightOperand, context);
      if (rightExpr == null) return null;

      // arithmetic operators:
      if (signOperator.Equals(myPredefined.BinaryPlusInt) ||
          signOperator.Equals(myPredefined.BinaryPlusUint) ||
          signOperator.Equals(myPredefined.BinaryPlusLong) ||
          signOperator.Equals(myPredefined.BinaryPlusUlong))
        return myContext.MkBVAdd(leftExpr, rightExpr); // todo: overflows

      if (signOperator.Equals(myPredefined.BinaryMinusInt) ||
          signOperator.Equals(myPredefined.BinaryMinusUint) ||
          signOperator.Equals(myPredefined.BinaryMinusLong) ||
          signOperator.Equals(myPredefined.BinaryMinusUlong))
        return myContext.MkBVSub(leftExpr, rightExpr); // todo: overflows

      if (signOperator.Equals(myPredefined.BinaryMultiplicationInt) ||
          signOperator.Equals(myPredefined.BinaryMultiplicationUint) ||
          signOperator.Equals(myPredefined.BinaryMultiplicationLong) ||
          signOperator.Equals(myPredefined.BinaryMultiplicationUlong))
        return myContext.MkBVMul(leftExpr, rightExpr); // todo: overflows

      if (signOperator.Equals(myPredefined.BinaryDivisionInt) ||
          signOperator.Equals(myPredefined.BinaryDivisionLong))
        return myContext.MkBVSDiv(leftExpr, rightExpr);

      if (signOperator.Equals(myPredefined.BinaryDivisionUint) ||
          signOperator.Equals(myPredefined.BinaryDivisionUlong))
        return myContext.MkBVUDiv(leftExpr, rightExpr);

      if (signOperator.Equals(myPredefined.BinaryRemainderInt) ||
          signOperator.Equals(myPredefined.BinaryRemainderLong))
        return myContext.MkBVSRem(leftExpr, rightExpr);

      if (signOperator.Equals(myPredefined.BinaryRemainderUint) ||
          signOperator.Equals(myPredefined.BinaryRemainderUlong))
        return myContext.MkBVURem(leftExpr, rightExpr);

      // logical operators:
      if (signOperator.Equals(myPredefined.BinaryLogicalAndInt) ||
          signOperator.Equals(myPredefined.BinaryLogicalAndUint) ||
          signOperator.Equals(myPredefined.BinaryLogicalAndLong) ||
          signOperator.Equals(myPredefined.BinaryLogicalAndUlong))
        return myContext.MkBVAND(leftExpr, rightExpr);

      if (signOperator.Equals(myPredefined.BinaryLogicalOrInt) ||
          signOperator.Equals(myPredefined.BinaryLogicalOrUint) ||
          signOperator.Equals(myPredefined.BinaryLogicalOrLong) ||
          signOperator.Equals(myPredefined.BinaryLogicalOrUlong))
        return myContext.MkBVOR(leftExpr, rightExpr);

      if (signOperator.Equals(myPredefined.BinaryLogicalXorInt) ||
          signOperator.Equals(myPredefined.BinaryLogicalXorUint) ||
          signOperator.Equals(myPredefined.BinaryLogicalXorLong) ||
          signOperator.Equals(myPredefined.BinaryLogicalXorUlong))
        return myContext.MkBVXOR(leftExpr, rightExpr);

      if (signOperator.Equals(myPredefined.BinaryLeftShiftInt) ||
          signOperator.Equals(myPredefined.BinaryLeftShiftUint) ||
          signOperator.Equals(myPredefined.BinaryLeftShiftLong) ||
          signOperator.Equals(myPredefined.BinaryLeftShiftUlong))
        return myContext.MkBVSHL(leftExpr, rightExpr);

      if (signOperator.Equals(myPredefined.BinaryRightShiftInt) ||
          signOperator.Equals(myPredefined.BinaryRightShiftUint) ||
          signOperator.Equals(myPredefined.BinaryRightShiftLong) ||
          signOperator.Equals(myPredefined.BinaryRightShiftUlong))
        return myContext.MkBVLSHR(leftExpr, rightExpr); // todo: check

      Logger.LogError("Unknown integer operator: {0}", signOperator);
      return null;
    }

    [CanBeNull, Pure]
    private BoolExpr VisitBooleanBinaryOperator(
      [NotNull] IBinaryExpression binaryExpression, [NotNull] RewriteContext context, [NotNull] IOperator signOperator)
    {
      var leftExpr = AcceptExpression(binaryExpression.LeftOperand, context);
      if (leftExpr == null) return null;

      var rightExpr = AcceptExpression(binaryExpression.RightOperand, context);
      if (rightExpr == null) return null;

      // todo: reduce the amount of casts?

      // boolean operators:
      if (signOperator.Equals(myPredefined.BinaryEqualityBool))
        return myContext.MkEq((BoolExpr) leftExpr, (BoolExpr) rightExpr);

      if (signOperator.Equals(myPredefined.BinaryInequalityBool))
        return myContext.MkNot(myContext.MkEq((BoolExpr) leftExpr, (BoolExpr) rightExpr));

      if (signOperator.Equals(myPredefined.BinaryLogicalAndBool) ||
          signOperator.Equals(myPredefined.BinaryConditionalLogicalAndAlsoBool))
        return myContext.MkAnd((BoolExpr) leftExpr, (BoolExpr) rightExpr);

      if (signOperator.Equals(myPredefined.BinaryLogicalOrBool) ||
          signOperator.Equals(myPredefined.BinaryConditionalLogicalOrElseBool))
        return myContext.MkOr((BoolExpr) leftExpr, (BoolExpr) rightExpr);

      if (signOperator.Equals(myPredefined.BinaryLogicalXorBool))
        return myContext.MkXor((BoolExpr) leftExpr, (BoolExpr) rightExpr);

      // equality operators:
      if (signOperator.Equals(myPredefined.BinaryEqualityInt) ||
          signOperator.Equals(myPredefined.BinaryEqualityUint) ||
          signOperator.Equals(myPredefined.BinaryEqualityLong) ||
          signOperator.Equals(myPredefined.BinaryEqualityUlong))
        return myContext.MkEq((BitVecExpr) leftExpr, (BitVecExpr) rightExpr);

      if (signOperator.Equals(myPredefined.BinaryEqualityFloat) ||
          signOperator.Equals(myPredefined.BinaryEqualityDouble))
        return myContext.MkFPEq((FPExpr) leftExpr, (FPExpr) rightExpr);

      // inequality operators:
      if (signOperator.Equals(myPredefined.BinaryInequalityInt) ||
          signOperator.Equals(myPredefined.BinaryInequalityUint) ||
          signOperator.Equals(myPredefined.BinaryInequalityLong) ||
          signOperator.Equals(myPredefined.BinaryInequalityUlong))
        return myContext.MkNot(myContext.MkEq((BitVecExpr) leftExpr, (BitVecExpr) rightExpr));

      if (signOperator.Equals(myPredefined.BinaryInequalityFloat) ||
          signOperator.Equals(myPredefined.BinaryInequalityDouble))
        return myContext.MkNot(myContext.MkFPEq((FPExpr) leftExpr, (FPExpr) rightExpr));

      // > and >= operators:
      if (signOperator.Equals(myPredefined.BinaryGreaterInt) ||
          signOperator.Equals(myPredefined.BinaryGreaterLong))
        return myContext.MkBVSGT((BitVecExpr) leftExpr, (BitVecExpr) rightExpr);

      if (signOperator.Equals(myPredefined.BinaryGreaterUint) ||
          signOperator.Equals(myPredefined.BinaryGreaterUlong))
        return myContext.MkBVUGT((BitVecExpr) leftExpr, (BitVecExpr) rightExpr);

      if (signOperator.Equals(myPredefined.BinaryGreaterEqualityInt) ||
          signOperator.Equals(myPredefined.BinaryGreaterEqualityLong))
        return myContext.MkBVSGE((BitVecExpr) leftExpr, (BitVecExpr) rightExpr);

      if (signOperator.Equals(myPredefined.BinaryGreaterEqualityUint) ||
          signOperator.Equals(myPredefined.BinaryGreaterEqualityUlong))
        return myContext.MkBVUGE((BitVecExpr) leftExpr, (BitVecExpr) rightExpr);

      if (signOperator.Equals(myPredefined.BinaryGreaterFloat) ||
          signOperator.Equals(myPredefined.BinaryGreaterDouble))
        return myContext.MkFPGt((FPExpr) leftExpr, (FPExpr) rightExpr);

      if (signOperator.Equals(myPredefined.BinaryGreaterEqualityFloat) ||
          signOperator.Equals(myPredefined.BinaryGreaterEqualityDouble))
        return myContext.MkFPGEq((FPExpr) leftExpr, (FPExpr) rightExpr);

      // < and <= operators:
      if (signOperator.Equals(myPredefined.BinaryLessInt) ||
          signOperator.Equals(myPredefined.BinaryLessLong))
        return myContext.MkBVSLT((BitVecExpr) leftExpr, (BitVecExpr) rightExpr);

      if (signOperator.Equals(myPredefined.BinaryLessUint) ||
          signOperator.Equals(myPredefined.BinaryLessUlong))
        return myContext.MkBVULT((BitVecExpr) leftExpr, (BitVecExpr) rightExpr);

      if (signOperator.Equals(myPredefined.BinaryLessEqualityInt) ||
          signOperator.Equals(myPredefined.BinaryLessEqualityLong))
        return myContext.MkBVSLE((BitVecExpr) leftExpr, (BitVecExpr) rightExpr);

      if (signOperator.Equals(myPredefined.BinaryLessEqualityUint) ||
          signOperator.Equals(myPredefined.BinaryLessEqualityUlong))
        return myContext.MkBVULE((BitVecExpr) leftExpr, (BitVecExpr) rightExpr);

      if (signOperator.Equals(myPredefined.BinaryLessFloat) ||
          signOperator.Equals(myPredefined.BinaryLessDouble))
        return myContext.MkFPLt((FPExpr) leftExpr, (FPExpr) rightExpr);

      if (signOperator.Equals(myPredefined.BinaryLessEqualityFloat) ||
          signOperator.Equals(myPredefined.BinaryLessEqualityDouble))
        return myContext.MkFPLEq((FPExpr) leftExpr, (FPExpr) rightExpr);

      // not supported boolean operators:
      if (signOperator.Equals(myPredefined.BinaryEqualityDecimal) ||
          signOperator.Equals(myPredefined.BinaryEqualityDelegate) ||
          signOperator.Equals(myPredefined.BinaryEqualityNullable) ||
          signOperator.Equals(myPredefined.BinaryEqualityString) ||
          signOperator.Equals(myPredefined.BinaryEqualityReference))
      {
        return null;
      }

      Logger.LogError("Unknown boolean operator: {0}", signOperator);
      return null;
    }

    [CanBeNull, Pure]
    private FPExpr VisitFloatingBinaryOperator(
      [NotNull] IBinaryExpression binaryExpression, [NotNull] RewriteContext context, [NotNull] IOperator signOperator)
    {
      var leftExpr = (FPExpr) AcceptExpression(binaryExpression.LeftOperand, context);
      if (leftExpr == null) return null;

      var rightExpr = (FPExpr) AcceptExpression(binaryExpression.RightOperand, context);
      if (rightExpr == null) return null;

      if (signOperator.Equals(myPredefined.BinaryPlusFloat) ||
          signOperator.Equals(myPredefined.BinaryPlusDouble))
        return myContext.MkFPAdd(GlobalRoundingMode, leftExpr, rightExpr);

      if (signOperator.Equals(myPredefined.BinaryMinusFloat) ||
          signOperator.Equals(myPredefined.BinaryMinusDouble))
        return myContext.MkFPSub(GlobalRoundingMode, leftExpr, rightExpr);

      if (signOperator.Equals(myPredefined.BinaryMultiplicationFloat) ||
          signOperator.Equals(myPredefined.BinaryMultiplicationDouble))
        return myContext.MkFPMul(GlobalRoundingMode, leftExpr, rightExpr);

      if (signOperator.Equals(myPredefined.BinaryDivisionFloat) ||
          signOperator.Equals(myPredefined.BinaryDivisionDouble))
        return myContext.MkFPDiv(GlobalRoundingMode, leftExpr, rightExpr);

      if (signOperator.Equals(myPredefined.BinaryRemainderFloat) ||
          signOperator.Equals(myPredefined.BinaryRemainderDouble))
        return myContext.MkFPRem(leftExpr, rightExpr);

      Logger.LogError("Unknown floating point operator: {0}", signOperator);
      return null;
    }

    public override Expr VisitUnaryOperatorExpression(IUnaryOperatorExpression unaryOperatorExpression, RewriteContext context)
    {
      var unaryConstant = base.VisitUnaryOperatorExpression(unaryOperatorExpression, context);
      if (unaryConstant != null) return unaryConstant;

      var operatorReference = unaryOperatorExpression.OperatorReference;
      if (operatorReference == null) return null;

      var resolveResult = operatorReference.Resolve();
      if (resolveResult.ResolveErrorType != ResolveErrorType.OK) return null;

      var signOperator = resolveResult.DeclaredElement as ISignOperator;
      if (signOperator == null || !signOperator.IsPredefined) return null;

      var operandExpr = AcceptExpression(unaryOperatorExpression.Operand, context);
      if (operandExpr == null) return null;

      //var returnType = signOperator.ReturnType;

      if (signOperator.Equals(myPredefined.UnaryBitwiseComplementInt) ||
          signOperator.Equals(myPredefined.UnaryBitwiseComplementUint) ||
          signOperator.Equals(myPredefined.UnaryBitwiseComplementLong) ||
          signOperator.Equals(myPredefined.UnaryBitwiseComplementUlong))
        return myContext.MkBVNot((BitVecExpr) operandExpr);

      if (signOperator.Equals(myPredefined.UnaryLogicalNegation))
        return myContext.MkNot((BoolExpr) operandExpr);

      if (signOperator.Equals(myPredefined.UnaryMinusInt) ||
          signOperator.Equals(myPredefined.UnaryMinusLong))
        return myContext.MkBVNeg((BitVecExpr) operandExpr);

      if (signOperator.Equals(myPredefined.UnaryPlusInt) ||
          signOperator.Equals(myPredefined.UnaryPlusLong))
        return (BitVecExpr) operandExpr;

      if (signOperator.Equals(myPredefined.UnaryMinusFloat) ||
          signOperator.Equals(myPredefined.UnaryMinusDouble))
        return myContext.MkFPNeg((FPExpr) operandExpr);

      if (signOperator.Equals(myPredefined.UnaryPlusFloat) ||
          signOperator.Equals(myPredefined.UnaryPlusDouble))
        return (FPExpr) operandExpr;

      // todo: handle increment/decrement

      return null;
    }

    private Expr VisitCast()
    {
      //myContext.mkbv

      //myContext.MkFPToFP()

      //myContext.mkfp

      return null;
    }

    [CanBeNull, Pure]
    private Expr AcceptExpression([CanBeNull] ICSharpExpression expression, RewriteContext context)
    {
      if (expression == null) return null;

      var result = expression.Accept(this, context);
      if (result != null)
      {
        var implicitlyConvertedTo = expression.GetImplicitlyConvertedTo();
        if (!implicitlyConvertedTo.IsUnknown)
        {
          // implicit cast
        }

        return result;
      }

      var expressionType = expression.GetExpressionType().ToIType();
      if (expressionType == null) return null;

      // todo: should not do variable from expr if failed to make expression from constant
      // todo: reset all the assigned variables (think of 'a + F(a++) + b')

      var variableName = "variable from expr: '" + expression.GetText() + "'";
      var variableSort = GetSort(expressionType);

      return myContext.MkConst(variableName, variableSort);
    }

    public Solver Solve()
    {
      return myContext.MkSolver();
    }

    public void Dispose()
    {
      foreach (var pair in myFreeVariables)
      {
        pair.Value.Dispose();
      }
    }
  }

  public class RewriteContext
  {
    // todo: checked context?
  }
}