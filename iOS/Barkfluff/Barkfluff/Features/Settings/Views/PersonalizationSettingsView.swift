//
//  PersonalizationSettingsView.swift
//  Barkfluff (iOS)
//
//  Персонализация чата: радиус пузыря, размытие/затемнение фона.
//

import SwiftUI

struct PersonalizationSettingsView: View {
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        @Bindable var settings = container.personalizationSettings

        Form {
            Section("Сообщения") {
                Stepper(
                    "Скругление пузыря: \(settings.bubbleCornerRadius) pt",
                    value: $settings.bubbleCornerRadius,
                    in: 0...30
                )
            }

            Section("Фон чата") {
                Toggle("Размытие фона", isOn: $settings.backgroundBlurEnabled)
                if settings.backgroundBlurEnabled {
                    Stepper(
                        "Радиус: \(settings.backgroundBlurRadius)",
                        value: $settings.backgroundBlurRadius,
                        in: 1...25
                    )
                }
                Stepper(
                    "Затемнение: \(settings.backgroundDimPercent)%",
                    value: $settings.backgroundDimPercent,
                    in: 0...100,
                    step: 5
                )
            }
        }
        .navigationTitle("Персонализация")
        .navigationBarTitleDisplayMode(.inline)
    }
}
