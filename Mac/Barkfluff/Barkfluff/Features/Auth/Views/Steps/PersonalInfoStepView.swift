//
//  PersonalInfoStepView.swift
//  Barkfluff
//
//  Шаг 1: Личные данные (Имя, Фамилия)
//

import SwiftUI

/// Шаг регистрации: ввод имени и фамилии
struct PersonalInfoStepView: View {
    @Bindable var data: RegistrationData

    @State private var firstNameError: String?
    @State private var lastNameError: String?

    var isValid: Bool {
        firstNameError == nil && lastNameError == nil &&
        !data.firstName.isEmpty && data.firstName.count >= 3
    }

    var body: some View {
        VStack(spacing: Theme.Spacing.lg) {
            // Иконка
            Image(systemName: "person.circle.fill")
                .font(.system(size: 60))
                .foregroundStyle(Color.accentColor)

            // Поля ввода в glass-контейнере
            VStack(spacing: Theme.Spacing.md) {
                // Имя
                VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                    TextField("Имя", text: $data.firstName)
                        .textFieldStyle(.roundedBorder)
                        .onChange(of: data.firstName) { _, newValue in
                            validateFirstName(newValue)
                        }

                    if let error = firstNameError {
                        Text(error)
                            .font(.caption)
                            .foregroundStyle(.red)
                    }
                }

                // Фамилия
                VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                    TextField("Фамилия", text: $data.lastName)
                        .textFieldStyle(.roundedBorder)
                        .onChange(of: data.lastName) { _, newValue in
                            validateLastName(newValue)
                        }

                    if let error = lastNameError {
                        Text(error)
                            .font(.caption)
                            .foregroundStyle(.red)
                    }
                }
            }
            .frame(maxWidth: 300)
        }
        .padding(Theme.Spacing.xl)
        .glassEffect(.regular, in: RoundedRectangle(cornerRadius: Theme.Radius.xl))
        .padding(Theme.Spacing.md) // Отступ для тени glass
        .onAppear {
            validateFirstName(data.firstName)
            validateLastName(data.lastName)
        }
    }

    private func validateFirstName(_ value: String) {
        let result = NameValidator.validateFirstName(value)
        firstNameError = result.error
    }

    private func validateLastName(_ value: String) {
        let result = NameValidator.validateLastName(value)
        lastNameError = result.error
    }
}

#Preview {
    PersonalInfoStepView(data: RegistrationData())
        .padding()
}
