import { FieldErrors } from "react-hook-form"
import { PmvrSystemUserUpdateRequest } from "@/types/api.types"

interface UsePmvrSystemUserUpdateFieldsProps {
  errors: FieldErrors<PmvrSystemUserUpdateRequest>
  disabledFields?: string[]
}

export default function usePmvrSystemUserUpdateFields(props: UsePmvrSystemUserUpdateFieldsProps) {
  const { errors, disabledFields = [] } = props

  return {
    mobileNumber: {
      props: {
        id: "system-user-mobileNumber",
        name: "mobileNumber",
        heading: "Mobile Number",
        type: "phone" as const,
        placeholder: "Enter mobile number...",
        disabled: disabledFields.includes("mobileNumber"),
        error: errors.mobileNumber,
      },
      rules: { },
    },
  }
}
