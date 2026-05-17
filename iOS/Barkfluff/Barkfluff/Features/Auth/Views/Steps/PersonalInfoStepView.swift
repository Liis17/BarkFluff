//
//  PersonalInfoStepView.swift
//  Barkfluff
//
//  Шаг 1: Личные данные (iOS)
//

import SwiftUI

struct PersonalInfoStepView: View {
    @Bindable var data: RegistrationData

    @FocusState private var focusedField: Field?

    enum Field: Hashable {
        case firstName
        case lastName
    }

    var body: some View {
        VStack(spacing: 16) {
            // Имя
            VStack(alignment: .leading, spacing: 4) {
                Text("auth.register.step.personal_info.first_name.label")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                TextField("auth.register.step.personal_info.first_name.placeholder", text: $data.firstName)
                    .textFieldStyle(.roundedBorder)
                    .textContentType(.givenName)
                    .focused($focusedField, equals: .firstName)
            }

            // Фамилия
            VStack(alignment: .leading, spacing: 4) {
                Text("auth.register.step.personal_info.last_name.label")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                TextField("auth.register.step.personal_info.last_name.placeholder", text: $data.lastName)
                    .textFieldStyle(.roundedBorder)
                    .textContentType(.familyName)
                    .focused($focusedField, equals: .lastName)
            }
        }
        .onAppear {
            focusedField = .firstName
        }
    }
}

#Preview {
    PersonalInfoStepView(data: RegistrationData())
        .padding()
}
