//
//  StepProgressIndicator.swift
//  Barkfluff
//
//  Индикатор прогресса регистрации (9 точек)
//

import SwiftUI

/// Индикатор прогресса для шагов регистрации
struct StepProgressIndicator: View {
    let currentStep: RegistrationStep
    let totalSteps: Int = 9

    var body: some View {
        HStack(spacing: Theme.Spacing.sm) {
            ForEach(1...totalSteps, id: \.self) { stepNumber in
                if let step = RegistrationStep(rawValue: stepNumber) {
                    stepIndicator(for: step)
                }
            }
        }
        .padding(.vertical, Theme.Spacing.md)
    }

    @ViewBuilder
    private func stepIndicator(for step: RegistrationStep) -> some View {
        let isCompleted = step.rawValue < currentStep.rawValue
        let isCurrent = step.rawValue == currentStep.rawValue
        let isOptional = step.isOptional

        ZStack {
            if isCompleted {
                Circle()
                    .fill(Color.accentColor)
                    .frame(width: 24, height: 24)
                Image(systemName: "checkmark")
                    .font(.system(size: 10, weight: .bold))
                    .foregroundStyle(.white)
            } else if isCurrent {
                Circle()
                    .strokeBorder(Color.accentColor, lineWidth: 2)
                    .frame(width: 24, height: 24)
                Text("\(step.rawValue)")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(Color.accentColor)
            } else {
                Circle()
                    .fill(isOptional ? Color.gray.opacity(0.3) : Color.gray.opacity(0.5))
                    .frame(width: 24, height: 24)
                Text("\(step.rawValue)")
                    .font(.system(size: 11, weight: .medium))
                    .foregroundStyle(.gray)
            }
        }
    }
}

/// Вертикальный индикатор прогресса (для компактного отображения)
struct VerticalStepProgressIndicator: View {
    let currentStep: RegistrationStep
    let totalSteps: Int = 9

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.sm) {
            ForEach(1...totalSteps, id: \.self) { stepNumber in
                if let step = RegistrationStep(rawValue: stepNumber) {
                    stepRow(for: step)
                }
            }
        }
    }

    @ViewBuilder
    private func stepRow(for step: RegistrationStep) -> some View {
        let isCompleted = step.rawValue < currentStep.rawValue
        let isCurrent = step.rawValue == currentStep.rawValue

        HStack(spacing: Theme.Spacing.sm) {
            // Индикатор
            ZStack {
                if isCompleted {
                    Circle()
                        .fill(Color.accentColor)
                        .frame(width: 20, height: 20)
                    Image(systemName: "checkmark")
                        .font(.system(size: 9, weight: .bold))
                        .foregroundStyle(.white)
                } else if isCurrent {
                    Circle()
                        .strokeBorder(Color.accentColor, lineWidth: 2)
                        .frame(width: 20, height: 20)
                } else {
                    Circle()
                        .fill(Color.gray.opacity(0.3))
                        .frame(width: 20, height: 20)
                }
            }

            // Название шага
            Text(step.title)
                .font(.system(size: 12))
                .foregroundStyle(isCurrent ? .primary : .secondary)

            if step.isOptional && !isCompleted && !isCurrent {
                Text("auth.register.step.optional_suffix")
                    .font(.system(size: 10))
                    .foregroundStyle(.tertiary)
            }

            Spacer()
        }
        .opacity(isCompleted ? 0.7 : 1.0)
    }
}

/// Прогресс-бар для регистрации
struct RegistrationProgressBar: View {
    let currentStep: RegistrationStep

    var body: some View {
        VStack(spacing: Theme.Spacing.xs) {
            // Прогресс-бар
            GeometryReader { geometry in
                ZStack(alignment: .leading) {
                    RoundedRectangle(cornerRadius: 4)
                        .fill(Color.gray.opacity(0.2))
                        .frame(height: 8)

                    RoundedRectangle(cornerRadius: 4)
                        .fill(Color.accentColor)
                        .frame(width: geometry.size.width * currentStep.progress, height: 8)
                }
            }
            .frame(height: 8)

            // Текст прогресса
            HStack {
                Text("auth.register.progress.step \(currentStep.rawValue) \(RegistrationStep.allCases.count)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Text(verbatim: "\(Int(currentStep.progress * 100))%")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }
}

#Preview("Horizontal") {
    VStack {
        StepProgressIndicator(currentStep: .email)
        StepProgressIndicator(currentStep: .password)
        StepProgressIndicator(currentStep: .completion)
    }
    .padding()
}

#Preview("Vertical") {
    VerticalStepProgressIndicator(currentStep: .password)
        .padding()
        .frame(width: 200)
}

#Preview("Progress Bar") {
    VStack(spacing: 20) {
        RegistrationProgressBar(currentStep: .personalInfo)
        RegistrationProgressBar(currentStep: .password)
        RegistrationProgressBar(currentStep: .completion)
    }
    .padding()
}
